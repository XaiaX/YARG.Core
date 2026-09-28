using System;
using System.IO;
using NUnit.Framework;
using YARG.Core.Engine.Drums;
using YARG.Core.IO;
using YARG.Core.Replays;

namespace YARG.Core.UnitTests.Replays;

[TestFixture]
public sealed class DrumsOptionalPedalStatsTests
{
    private static FixedArrayStream Stream(byte[] bytes)
    {
        var array = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(array.Span);
        return new FixedArrayStream(array);
    }

    private static byte[] Serialize(DrumsStats stats)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        stats.Serialize(writer);
        return memory.ToArray();
    }

    private static byte[] Serialize(DrumsReplayStats stats)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        stats.Serialize(writer);
        return memory.ToArray();
    }

    [Test]
    public void DrumFrameStats_Version21PreservesOptionalCountersAndVersion20DefaultsThem()
    {
        var original = new DrumsStats
        {
            TotalNotes = 12, NotesHit = 10, OptionalPedalNotes = 4, OptionalPedalHits = 1,
            AssistedPedalNotes = 2, OptionalPedalAccuracyEnabled = true,
        };
        var bytes = Serialize(original);
        var currentStream = Stream(bytes);
        var current = new DrumsStats(ref currentStream, ReplayIO.OPTIONAL_PEDAL_STATS_MIN);
        var legacyStream = Stream(bytes[..^13]); // Three ints and one bool were appended in v21.
        var legacy = new DrumsStats(ref legacyStream, ReplayIO.ELITE_FILL_MIN);

        Assert.Multiple(() =>
        {
            Assert.That(current.OptionalPedalNotes, Is.EqualTo(4));
            Assert.That(current.OptionalPedalHits, Is.EqualTo(1));
            Assert.That(current.AssistedPedalNotes, Is.EqualTo(2));
            Assert.That(current.OptionalPedalAccuracyEnabled, Is.True);
            Assert.That(current.TotalNotes, Is.EqualTo(12));
            Assert.That(legacy.TotalNotes, Is.EqualTo(12));
            Assert.That(legacy.OptionalPedalNotes, Is.Zero);
            Assert.That(legacy.OptionalPedalHits, Is.Zero);
            Assert.That(legacy.AssistedPedalNotes, Is.Zero);
            Assert.That(legacy.OptionalPedalAccuracyEnabled, Is.False);
        });
    }

    [Test]
    public void ReplayMetadata_Version21PreservesCountersAndVersion20DefaultsThem()
    {
        var stats = new DrumsStats
        {
            TotalNotes = 10, NotesHit = 8, OptionalPedalNotes = 3, OptionalPedalHits = 1,
            AssistedPedalNotes = 1, OptionalPedalAccuracyEnabled = true,
        };
        var bytes = Serialize(new DrumsReplayStats("drummer", false, stats));
        // Metadata starts with a game-mode byte, consumed by ReplayInfo before this constructor.
        var currentStream = Stream(bytes[1..]);
        var current = new DrumsReplayStats(ref currentStream, ReplayIO.OPTIONAL_PEDAL_STATS_MIN);
        var legacyStream = Stream(bytes[1..^13]);
        var legacy = new DrumsReplayStats(ref legacyStream, ReplayIO.ELITE_FILL_MIN);

        Assert.Multiple(() =>
        {
            Assert.That(current.PercentageHit, Is.EqualTo(80f));
            Assert.That(current.OptionalPedalNotes, Is.EqualTo(3));
            Assert.That(current.OptionalPedalHits, Is.EqualTo(1));
            Assert.That(current.AssistedPedalNotes, Is.EqualTo(1));
            Assert.That(current.OptionalPedalAccuracyEnabled, Is.True);
            Assert.That(legacy.PercentageHit, Is.EqualTo(80f));
            Assert.That(legacy.OptionalPedalNotes, Is.Zero);
            Assert.That(legacy.OptionalPedalHits, Is.Zero);
            Assert.That(legacy.AssistedPedalNotes, Is.Zero);
            Assert.That(legacy.OptionalPedalAccuracyEnabled, Is.False);
        });
    }

    [Test]
    public void EmptyChartPercentageIsFiniteForLiveAndDeserializedMetadata()
    {
        var live = new DrumsReplayStats("drummer", false, new DrumsStats());
        var bytes = Serialize(live);
        var stream = Stream(bytes[1..]);
        var read = new DrumsReplayStats(ref stream, ReplayIO.REPLAY_VERSIONS.CURRENT);
        Assert.Multiple(() =>
        {
            Assert.That(live.PercentageHit, Is.EqualTo(100f));
            Assert.That(read.PercentageHit, Is.EqualTo(100f));
            Assert.That(float.IsFinite(read.PercentageHit), Is.True);
        });
    }

    [Test]
    public void AssistedModeFullComboRequiresNoRequiredMissesAndLegacyModeUsesCombo()
    {
        var stats = new DrumsStats
        {
            TotalNotes = 7, NotesHit = 7, MaxCombo = 6, OptionalPedalNotes = 3,
            OptionalPedalHits = 1, AssistedPedalNotes = 2, OptionalPedalAccuracyEnabled = true,
        };
        Assert.That(stats.IsFullCombo, Is.True);
        stats.NotesHit = 6;
        Assert.That(stats.IsFullCombo, Is.False);
        stats.OptionalPedalAccuracyEnabled = false;
        stats.NotesHit = 7;
        Assert.That(stats.IsFullCombo, Is.False);
        stats.MaxCombo = 7;
        Assert.That(stats.IsFullCombo, Is.True);
    }

    [Test]
    public void CopyAndResetPreserveModeAndChartTotalButClearRunCounters()
    {
        var stats = new DrumsStats
        {
            OptionalPedalNotes = 3, OptionalPedalHits = 1, AssistedPedalNotes = 1,
            OptionalPedalAccuracyEnabled = true,
        };
        var copy = new DrumsStats(stats);
        Assert.Multiple(() =>
        {
            Assert.That(copy.OptionalPedalNotes, Is.EqualTo(3));
            Assert.That(copy.OptionalPedalHits, Is.EqualTo(1));
            Assert.That(copy.AssistedPedalNotes, Is.EqualTo(1));
            Assert.That(copy.OptionalPedalAccuracyEnabled, Is.True);
        });
        copy.Reset();
        Assert.Multiple(() =>
        {
            Assert.That(copy.OptionalPedalNotes, Is.EqualTo(3));
            Assert.That(copy.OptionalPedalHits, Is.Zero);
            Assert.That(copy.AssistedPedalNotes, Is.Zero);
            Assert.That(copy.OptionalPedalAccuracyEnabled, Is.True);
        });
    }
}
