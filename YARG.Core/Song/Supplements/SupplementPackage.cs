// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace YARG.Core.Song.Supplements
{
    /// <summary>A stable open ZIP snapshot. Index reads never inflate asset payloads.</summary>
    public sealed class SupplementPackage : IDisposable
    {
        internal static Action<string>? OpenObservation;
        internal const int MAX_MANIFEST_BYTES = 64 * 1024 * 1024;
        internal const int MAX_DTA_BYTES = 32 * 1024 * 1024;
        internal const long MAX_ASSET_BYTES = 1024L * 1024 * 1024;
        internal const int MAX_ENTRIES = 200000;
        private readonly ZipArchive _archive;
        private readonly SupplementPackageIndex _index;
        private IReadOnlyDictionary<string, SupplementAsset> _assets => _index.AssetMap;
        private byte[] _dta => _index.Dta;
        private int _leases = 1;
        private bool _disposed;
        public SupplementSourceKind Kind => _index.Kind;
        public string ManifestHash => _index.ManifestHash;
        public byte[] DtaBytes => (byte[])_dta.Clone();
        public string DtaPath => _index.DtaPath;
        public IReadOnlyList<string> Definitions => _index.Definitions;
        public IReadOnlyList<SupplementAsset> Assets => _index.Assets;

        private SupplementPackage(ZipArchive archive)
        {
            _archive = archive;
            if (archive.Entries.Count > MAX_ENTRIES) throw new InvalidDataException("Too many package entries.");
            var entryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                SupplementPaths.Validate(entry.FullName);
                if (!entryPaths.Add(entry.FullName)) throw new InvalidDataException("Duplicate package entry: " + entry.FullName);
            }
            byte[] manifestBytes = ReadBounded(Required("manifest.json"), MAX_MANIFEST_BYTES);
            string manifestHash = Hash(manifestBytes);
            byte[] currentDta = ReadBounded(Required("sources/original.dta"), MAX_DTA_BYTES);
            _index = SupplementPackageIndexCache.Get(manifestHash, () =>
                SupplementPackageIndex.Parse(manifestBytes, manifestHash, currentDta));
            // A cache hit proves the manifest, not this archive's current contents. Check
            // the DTA and referenced blob lengths on every open, even if timestamps match.
            if (currentDta.LongLength != _dta.LongLength || Hash(currentDta) != _index.DtaSha256)
                throw new InvalidDataException("Package DTA digest mismatch.");
            foreach (var asset in Assets)
                if (Required(asset.Blob).Length != asset.Length)
                    throw new InvalidDataException("Asset length differs from manifest: " + asset.Path);
        }

        public static SupplementPackage Open(string path)
        {
            OpenObservation?.Invoke(path);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ZipArchive? archive = null;
            try { archive = new ZipArchive(stream, ZipArchiveMode.Read); return new SupplementPackage(archive); }
            catch (Exception error) when (error is JsonException || error is DecoderFallbackException) { archive?.Dispose(); stream.Dispose(); throw new InvalidDataException("Invalid package manifest.", error); }
            catch { archive?.Dispose(); stream.Dispose(); throw; }
        }
        public bool ContainsAsset(string relativePath) => _assets.ContainsKey(relativePath);
        public bool TryGetAsset(string relativePath, out SupplementAsset asset) => _assets.TryGetValue(relativePath, out asset!);
        public byte[] ReadAsset(string relativePath)
        {
            if (!_assets.TryGetValue(relativePath, out var asset)) throw new FileNotFoundException("Package asset missing: " + relativePath);
            byte[] bytes;
            lock (_archive) bytes = ReadBounded(Required(asset.Blob), asset.Length);
            if (bytes.LongLength != asset.Length || Hash(bytes) != asset.Sha256) throw new InvalidDataException("Package asset digest mismatch: " + relativePath);
            return bytes;
        }
        /// <summary>Verify using bounded buffers, then return a stream which owns an archive lease.
        /// The stream remains valid after this package is disposed. Reads serialize archive access.</summary>
        public Stream OpenAsset(string relativePath)
        {
            lock (_archive)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SupplementPackage));
                if (!_assets.TryGetValue(relativePath, out var asset)) throw new FileNotFoundException("Package asset missing: " + relativePath);
                var entry = Required(asset.Blob);
                using (var stream = entry.Open())
                using (var hash = SHA256.Create())
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int count;
                    while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        total += count;
                        if (total > asset.Length) throw new InvalidDataException("Package asset exceeds declared length: " + relativePath);
                        hash.TransformBlock(buffer, 0, count, null, 0);
                    }
                    hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    string digest = BitConverter.ToString(hash.Hash!).Replace("-", "").ToLowerInvariant();
                    if (total != asset.Length || digest != asset.Sha256) throw new InvalidDataException("Package asset digest mismatch: " + relativePath);
                }
                var source = entry.Open();
                _leases++;
                return new AssetStream(this, entry, source, asset.Length);
            }
        }
        public void Verify()
        {
            foreach (var asset in Assets) { using var stream = OpenAsset(asset.Path); }
        }
        /// <summary>Extract to a new directory atomically; original files and DTA bytes are preserved.</summary>
        public void Extract(string directory)
        {
            string target = Path.GetFullPath(directory);
            if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Extraction destination already exists: " + target);
            string staging = target + ".tmp-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            try
            {
                WriteNew(staging, DtaPath, _dta);
                foreach (var asset in Assets)
                {
                    string destination = Path.Combine(staging, asset.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var input = OpenAsset(asset.Path);
                    using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                    input.CopyTo(output);
                }
                Directory.Move(staging, target);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
        private static void WriteNew(string root, string relative, byte[] bytes)
        {
            SupplementPaths.Validate(relative);
            string destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            stream.Write(bytes, 0, bytes.Length);
        }
        private ZipArchiveEntry Required(string name) => _archive.GetEntry(name) ?? throw new InvalidDataException("Missing package entry: " + name);
        internal static byte[] ReadBounded(ZipArchiveEntry entry, long limit)
        {
            if (entry.Length < 0 || entry.Length > limit || entry.Length > int.MaxValue) throw new InvalidDataException("Package entry exceeds size limit: " + entry.FullName);
            using var source = entry.Open();
            byte[] bytes = new byte[(int)entry.Length];
            int position = 0;
            while (position < bytes.Length)
            {
                int count = source.Read(bytes, position, bytes.Length - position);
                if (count == 0) throw new InvalidDataException("Truncated package entry: " + entry.FullName);
                position += count;
            }
            if (source.ReadByte() != -1) throw new InvalidDataException("Expanded package entry exceeds declared length: " + entry.FullName);
            return bytes;
        }
        internal static string Hash(byte[] bytes)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public void Dispose()
        {
            lock (_archive)
            {
                if (_disposed) return;
                _disposed = true;
                if (--_leases == 0) _archive.Dispose();
            }
        }
        private sealed class AssetStream : Stream
        {
            private readonly SupplementPackage _owner;
            private Stream? _source;
            private readonly long _length;
            private readonly ZipArchiveEntry _entry;
            private long _position;
            internal AssetStream(SupplementPackage owner, ZipArchiveEntry entry, Stream source, long length) { _owner = owner; _entry = entry; _source = source; _length = length; }
            public override bool CanRead => _source != null;
            public override bool CanWrite => false;
            public override bool CanSeek => _source != null;
            public override long Length => _length;
            public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                lock (_owner._archive)
                {
                    if (_source == null) throw new ObjectDisposedException(nameof(AssetStream));
                    int read = _source.Read(buffer, offset, count); _position += read; return read;
                }
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin)
            {
                lock (_owner._archive)
                {
                    if (_source == null) throw new ObjectDisposedException(nameof(AssetStream));
                    long target;
                    try { target = checked(origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _position + offset,
                        SeekOrigin.End => _length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }); }
                    catch (OverflowException error) { throw new IOException("Seek outside asset bounds.", error); }
                    if (target < 0 || target > _length) throw new IOException("Seek outside asset bounds.");
                    if (target < _position) { _source.Dispose(); _source = _entry.Open(); _position = 0; }
                    if (target > _position)
                    {
                        var buffer = new byte[81920];
                        while (_position < target)
                        {
                            int count = _source.Read(buffer, 0, (int)Math.Min(buffer.Length, target - _position));
                            if (count == 0) throw new EndOfStreamException();
                            _position += count;
                        }
                    }
                    return _position;
                }
            }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing) lock (_owner._archive)
                {
                    if (_source != null)
                    {
                        _source.Dispose(); _source = null;
                        if (--_owner._leases == 0) _owner._archive.Dispose();
                    }
                }
                base.Dispose(disposing);
            }
        }
    }
}
