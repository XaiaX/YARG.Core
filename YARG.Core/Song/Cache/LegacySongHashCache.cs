// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using YARG.Core.IO;

namespace YARG.Core.Song.Cache
{
    /// <summary>Identity of original, uncompressed MIDI bytes, independent of their location.</summary>
    public sealed class MidiContentFingerprint : IEquatable<MidiContentFingerprint>
    {
        public string Sha256 { get; }
        public long Length { get; }

        public MidiContentFingerprint(string sha256, long length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (sha256 == null || sha256.Length != 64 || sha256.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("Expected a 32-byte SHA-256 hex digest", nameof(sha256));
            Sha256 = sha256.ToUpperInvariant();
            Length = length;
        }

        public static MidiContentFingerprint Compute(ReadOnlySpan<byte> bytes)
        {
            using var algorithm = SHA256.Create();
            Span<byte> digest = stackalloc byte[32];
            if (!algorithm.TryComputeHash(bytes, digest, out _)) throw new CryptographicException();
            return new MidiContentFingerprint(BitConverter.ToString(digest.ToArray()).Replace("-", ""), bytes.Length);
        }

        public bool Equals(MidiContentFingerprint? other) => other != null && Length == other.Length && Sha256 == other.Sha256;
        public override bool Equals(object? obj) => obj is MidiContentFingerprint other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Sha256, Length);
    }

    /// <summary>The exact ordered inputs to the legacy concatenated-byte SHA-1 identity.</summary>
    public sealed class MidiInputVector : IEquatable<MidiInputVector>
    {
        public MidiContentFingerprint Base { get; }
        public IReadOnlyList<MidiContentFingerprint> Updates { get; }
        public MidiContentFingerprint? Upgrade { get; }

        public MidiInputVector(MidiContentFingerprint baseMidi, IEnumerable<MidiContentFingerprint> updates,
            MidiContentFingerprint? upgrade)
        {
            Base = baseMidi ?? throw new ArgumentNullException(nameof(baseMidi));
            var copy = updates?.ToArray() ?? throw new ArgumentNullException(nameof(updates));
            if (copy.Any(x => x == null)) throw new ArgumentException("Null update fingerprint", nameof(updates));
            Updates = Array.AsReadOnly(copy);
            Upgrade = upgrade;
        }

        public bool Equals(MidiInputVector? other) => other != null && Base.Equals(other.Base) &&
            Updates.SequenceEqual(other.Updates) && Equals(Upgrade, other.Upgrade);
        public override bool Equals(object? obj) => obj is MidiInputVector other && Equals(other);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Base);
            hash.Add(Updates.Count);
            foreach (var update in Updates) hash.Add(update);
            hash.Add(Upgrade);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Per-scan memoization of verified original-input identities. Export is staged with the library
    /// candidate; importing never mutates the previous scan's cache. This is not a file-validity cache.
    /// </summary>
    public sealed class LegacySongHashCache
    {
        private const int VERSION = 1;
        private const int MAX_ENTRIES = 1000000;
        private const int MAX_UPDATES = 4096;
        private readonly Dictionary<MidiInputVector, HashWrapper> _entries = new();
        private readonly object _gate = new();

        /// <remarks>The factory must hash the verified original bytes identified by the vector.</remarks>
        public HashWrapper GetOrAdd(MidiInputVector vector, Func<HashWrapper> factory)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (_gate)
            {
                if (_entries.TryGetValue(vector, out var existing)) return existing;
            }
            var result = factory();
            lock (_gate) _entries[vector] = result;
            return result;
        }

        public static HashWrapper HashOriginalInputs(IEnumerable<FixedArray<byte>> inputs)
        {
            using var algorithm = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            foreach (var input in inputs) algorithm.AppendData(input.ReadOnlySpan);
            return HashWrapper.Create(algorithm.GetHashAndReset());
        }

        // The optional inventory field is a bounded, checksummed snapshot. A damaged snapshot
        // misses entirely rather than poisoning an otherwise usable library cache.
        public string Export()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(VERSION);
            lock (_gate)
            {
                writer.Write(_entries.Count);
                foreach (var entry in _entries)
                {
                    WriteFingerprint(writer, entry.Key.Base);
                    writer.Write(entry.Key.Updates.Count);
                    foreach (var update in entry.Key.Updates) WriteFingerprint(writer, update);
                    writer.Write(entry.Key.Upgrade != null);
                    if (entry.Key.Upgrade != null) WriteFingerprint(writer, entry.Key.Upgrade);
                    writer.Write(entry.Value.HashBytes);
                }
            }
            writer.Flush();
            using var algorithm = SHA256.Create();
            byte[] checksum = algorithm.ComputeHash(stream.GetBuffer(), 0, (int) stream.Length);
            writer.Write(checksum);
            return Convert.ToBase64String(stream.ToArray());
        }

        public static LegacySongHashCache Import(string? snapshot)
        {
            var empty = new LegacySongHashCache();
            if (string.IsNullOrEmpty(snapshot)) return empty;
            try
            {
                // Each entry needs at least one fingerprint, counts/flag, and SHA-1.
                if (snapshot!.Length > 256 * 1024 * 1024) return empty;
                byte[] bytes = Convert.FromBase64String(snapshot);
                if (bytes.Length < 40) return empty;
                int payloadLength = bytes.Length - 32;
                using var algorithm = SHA256.Create();
                var checksum = algorithm.ComputeHash(bytes, 0, payloadLength);
                if (!checksum.AsSpan().SequenceEqual(bytes.AsSpan(payloadLength))) return empty;
                using var stream = new MemoryStream(bytes, 0, payloadLength, false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadInt32() != VERSION) return empty;
                int count = reader.ReadInt32();
                if (count < 0 || count > MAX_ENTRIES || count > payloadLength / 65) return empty;
                var result = new LegacySongHashCache();
                for (int i = 0; i < count; i++)
                {
                    var baseMidi = ReadFingerprint(reader);
                    int updateCount = reader.ReadInt32();
                    if (updateCount < 0 || updateCount > MAX_UPDATES || updateCount > (stream.Length - stream.Position) / 40)
                        return empty;
                    var updates = new MidiContentFingerprint[updateCount];
                    for (int j = 0; j < updates.Length; j++) updates[j] = ReadFingerprint(reader);
                    byte present = reader.ReadByte();
                    if (present > 1) return empty;
                    var upgrade = present == 1 ? ReadFingerprint(reader) : null;
                    byte[] hash = reader.ReadBytes(HashWrapper.HASH_SIZE_IN_BYTES);
                    if (hash.Length != HashWrapper.HASH_SIZE_IN_BYTES) return empty;
                    var vector = new MidiInputVector(baseMidi, updates, upgrade);
                    if (result._entries.ContainsKey(vector)) return empty;
                    result._entries.Add(vector, HashWrapper.Create(hash));
                }
                return stream.Position == stream.Length ? result : empty;
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException || ex is ArgumentException)
            {
                return empty;
            }
        }

        private static void WriteFingerprint(BinaryWriter writer, MidiContentFingerprint fingerprint)
        {
            writer.Write(fingerprint.Length);
            for (int i = 0; i < 64; i += 2) writer.Write(Convert.ToByte(fingerprint.Sha256.Substring(i, 2), 16));
        }

        private static MidiContentFingerprint ReadFingerprint(BinaryReader reader)
        {
            long length = reader.ReadInt64();
            byte[] digest = reader.ReadBytes(32);
            return new MidiContentFingerprint(BitConverter.ToString(digest).Replace("-", ""), length);
        }
    }
}
