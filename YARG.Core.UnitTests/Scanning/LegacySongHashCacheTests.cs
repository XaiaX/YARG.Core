// pattern: Functional Core
using System;
using System.Linq;
using NUnit.Framework;
using YARG.Core.IO;
using YARG.Core.Song;
using YARG.Core.Song.Cache;

namespace YARG.Core.UnitTests.Scanning;

public sealed class LegacySongHashCacheTests
{
    private static MidiContentFingerprint F(params byte[] bytes) => MidiContentFingerprint.Compute(bytes);

    [Test]
    public void VectorPreservesOrderRolesAndAbsence()
    {
        var a = F(1); var b = F(2); var empty = F();
        var vector = new MidiInputVector(a, new[] { b }, null);
        Assert.That(vector, Is.EqualTo(new MidiInputVector(a, new[] { b }, null)));
        Assert.That(vector, Is.Not.EqualTo(new MidiInputVector(b, new[] { a }, null)));
        Assert.That(vector, Is.Not.EqualTo(new MidiInputVector(a, Array.Empty<MidiContentFingerprint>(), b)));
        Assert.That(vector, Is.Not.EqualTo(new MidiInputVector(a, new[] { b }, empty)));
        Assert.That(new MidiInputVector(a, new[] { a, b }, null), Is.Not.EqualTo(new MidiInputVector(a, new[] { b, a }, null)));
    }

    [Test]
    public void FingerprintsValidateAndVectorsDefensivelyCopyInputs()
    {
        Assert.Throws<ArgumentException>(() => new MidiContentFingerprint("123", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MidiContentFingerprint(new string('0', 64), -1));
        var a = F(1); var b = F(2);
        var updates = new[] { a };
        var vector = new MidiInputVector(a, updates, null);
        updates[0] = b;
        Assert.That(vector.Updates[0], Is.EqualTo(a));
        Assert.That(new MidiContentFingerprint(a.Sha256.ToLowerInvariant(), a.Length), Is.EqualTo(a));
        Assert.That(F(1, 2), Is.Not.EqualTo(F(2, 1)));
    }

    [Test]
    public void CacheHitAndPersistedHitDoNotComputeAgain()
    {
        var vector = new MidiInputVector(F(1), new[] { F(2) }, F(3));
        var expected = HashWrapper.Hash(new byte[] { 1, 2, 3 });
        var cache = new LegacySongHashCache();
        Assert.That(cache.GetOrAdd(vector, () => expected), Is.EqualTo(expected));
        Assert.That(cache.GetOrAdd(vector, () => throw new Exception("Unexpected rehash")), Is.EqualTo(expected));
        var restored = LegacySongHashCache.Import(cache.Export());
        Assert.That(restored.GetOrAdd(vector, () => throw new Exception("Unexpected rehash")), Is.EqualTo(expected));
    }

    [Test]
    public void CorruptSnapshotIsSafeCacheMiss()
    {
        var vector = new MidiInputVector(F(1), Array.Empty<MidiContentFingerprint>(), null);
        var cache = new LegacySongHashCache();
        var expected = HashWrapper.Hash(new byte[] { 1 });
        cache.GetOrAdd(vector, () => expected);
        byte[] bytes = Convert.FromBase64String(cache.Export());
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] ^= 1;
            int calls = 0;
            var restored = LegacySongHashCache.Import(Convert.ToBase64String(bytes));
            Assert.That(restored.GetOrAdd(vector, () => { calls++; return expected; }), Is.EqualTo(expected));
            Assert.That(calls, Is.EqualTo(1), "Corrupted byte " + index);
            bytes[index] ^= 1;
        }
        Assert.DoesNotThrow(() => LegacySongHashCache.Import("not base64"));
    }

    [Test]
    public void IncrementalHashMatchesOriginalConcatenationForGeneratedInputs()
    {
        var random = new Random(7219);
        for (int trial = 0; trial < 100; trial++)
        {
            byte[][] inputs = Enumerable.Range(0, 1 + random.Next(8)).Select(_ =>
            {
                byte[] bytes = new byte[random.Next(200)]; random.NextBytes(bytes); return bytes;
            }).ToArray();
            var buffers = inputs.Select(x =>
            {
                var buffer = FixedArray<byte>.Alloc(x.Length); x.AsSpan().CopyTo(buffer.Span); return buffer;
            }).ToArray();
            try
            {
                Assert.That(LegacySongHashCache.HashOriginalInputs(buffers),
                    Is.EqualTo(HashWrapper.Hash(inputs.SelectMany(x => x).ToArray())));
            }
            finally { foreach (var buffer in buffers) buffer.Dispose(); }
        }
    }
}
