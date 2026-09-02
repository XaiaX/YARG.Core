using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonscraperChartEditor.Song.IO;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.IO;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Song;

/// <summary>
/// Regression coverage for the scan-time Elite Drums downchart metadata
/// (<see cref="AvailableParts.EliteDrumsDownchart"/>): it must record the loader's
/// actual "generated downchart is non-empty" notion, so the shared menu rules
/// (<see cref="EliteDrumsDownchartRules"/>) can never offer a target/difficulty whose
/// downchart would not be built. The tests feed real MIDI bytes through the real
/// scanner (<c>SongEntry.ParseMidi</c>) and then assert the rules verdicts on the
/// resulting parts; the companion loader assertions live in
/// <c>MoonSongLoaderTests_Drums.ForcedEliteDrumsDownchart_HatPedalOnlyChart_BuildsNoDownchart</c>.
/// </summary>
[TestFixture]
public sealed class EliteDrumsDownchartScanTests
{
    private const short RESOLUTION = 192;

    // Expert-difficulty Elite Drums note numbers (ELITE_DRUMS_DIFF_START_LOOKUP[Expert]
    // = 74; HatPedal = start - 2, HiHat = start + 2, Snare = start + 1).
    private const int EXPERT_HAT_PEDAL = 72;
    private const int EXPERT_HIHAT = 76;
    private const int EXPERT_SNARE = 75;

    // Easy-difficulty hat pedal (ELITE_DRUMS_DIFF_START_LOOKUP[Easy] = 2)
    private const int EASY_HAT_PEDAL = 0;

    // Hard-difficulty snare (ELITE_DRUMS_DIFF_START_LOOKUP[Hard] = 50)
    private const int HARD_SNARE = 51;

    // "Indifferent hat" marker for the Expert block (start + 14)
    private const int EXPERT_INDIFFERENT_HAT_MARKER = 88;

    private static readonly Instrument[] Targets =
    {
        Instrument.FourLaneDrums,
        Instrument.ProDrums,
        Instrument.FiveLaneDrums,
    };

    // ---- Minimal standard-MIDI-file writer (the DryWetMidi package referenced by
    // the test project is the "Nativeless" build, which cannot serialize files) ----

    private readonly struct TrackEvent(long tick, byte[] data)
    {
        public long Tick => tick;
        public byte[] Data => data;
    }

    private static void WriteVlq(List<byte> output, long value)
    {
        Span<byte> digits = stackalloc byte[5];
        var index = digits.Length - 1;
        digits[index] = (byte) (value & 0x7F);
        value >>= 7;
        while (value > 0)
        {
            index--;
            digits[index] = (byte) ((value & 0x7F) | 0x80);
            value >>= 7;
        }

        for (; index < digits.Length; index++)
        {
            output.Add(digits[index]);
        }
    }

    private static void WriteBE16(List<byte> output, int value)
    {
        output.Add((byte) (value >> 8));
        output.Add((byte) value);
    }

    private static void WriteBE32(List<byte> output, int value)
    {
        output.Add((byte) (value >> 24));
        output.Add((byte) (value >> 16));
        output.Add((byte) (value >> 8));
        output.Add((byte) value);
    }

    private static byte[] BuildTrackChunk(string name, IEnumerable<TrackEvent> events)
    {
        var payload = new List<byte>();

        // Track name meta event (delta 0): FF 03 <vlq length> <ascii>
        WriteVlq(payload, 0);
        payload.Add(0xFF);
        payload.Add(0x03);
        payload.Add((byte) name.Length);
        payload.AddRange(System.Text.Encoding.ASCII.GetBytes(name));

        var previousTick = 0L;
        foreach (var trackEvent in events.OrderBy(trackEvent => trackEvent.Tick))
        {
            WriteVlq(payload, trackEvent.Tick - previousTick);
            previousTick = trackEvent.Tick;
            payload.AddRange(trackEvent.Data);
        }

        // End of track (delta 0)
        WriteVlq(payload, 0);
        payload.Add(0xFF);
        payload.Add(0x2F);
        payload.Add(0x00);

        var chunk = new List<byte>();
        chunk.AddRange(System.Text.Encoding.ASCII.GetBytes("MTrk"));
        WriteBE32(chunk, payload.Count);
        chunk.AddRange(payload);
        return chunk.ToArray();
    }

    private static byte[] BuildMidiFile(params byte[][] tracks)
    {
        var file = new List<byte>();
        file.AddRange(System.Text.Encoding.ASCII.GetBytes("MThd"));
        WriteBE32(file, 6);
        WriteBE16(file, 1); // format 1
        WriteBE16(file, tracks.Length);
        WriteBE16(file, RESOLUTION);
        foreach (var track in tracks)
        {
            file.AddRange(track);
        }

        return file.ToArray();
    }

    /// <summary>A note-on/note-off pair on the given channel and tick (96-tick sustain by default).</summary>
    private static TrackEvent[] Note(int tick, int note, int velocity, int channel = 0, int length = 96)
    {
        return new[]
        {
            new TrackEvent(tick, new[] { (byte) (0x90 | channel), (byte) note, (byte) velocity }),
            new TrackEvent(tick + length, new[] { (byte) (0x80 | channel), (byte) note, (byte) 0 }),
        };
    }

    /// <summary>A text meta event (FF 01 &lt;vlq length&gt; ASCII), as the reader surfaces them.</summary>
    private static TrackEvent Text(int tick, string text)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        var data = new byte[3 + bytes.Length];
        data[0] = 0xFF;
        data[1] = 0x01;
        data[2] = (byte) bytes.Length;
        bytes.CopyTo(data, 3);
        return new TrackEvent(tick, data);
    }

    // Each Note(...) helper call yields a note-on/off pair; flatten single events
    // and groups into one stream for the track writer.
    private static byte[] EliteTrack(params object[] events)
    {
        var flattened = new List<TrackEvent>();
        foreach (var item in events)
        {
            switch (item)
            {
                case TrackEvent single:
                    flattened.Add(single);
                    break;
                case TrackEvent[] group:
                    flattened.AddRange(group);
                    break;
                default:
                    throw new ArgumentException($"Unsupported MIDI test event item: {item.GetType()}");
            }
        }

        return BuildTrackChunk(MidIOHelper.ELITE_DRUMS_TRACK, flattened);
    }

    private static AvailableParts ScanParts(params byte[][] tracks)
    {
        var bytes = BuildMidiFile(tracks);

        using var buffer = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(buffer.Span);
        return TestSongEntry.ParseMidiForTest(buffer);
    }

    private static TestSongEntry EntryFromParts(AvailableParts parts)
    {
        var entry = new TestSongEntry();
        entry.SetParts(parts);
        return entry;
    }

    [Test]
    public void UnforcedHatPedalsOnly_EliteActiveButNoDownchartMetadata()
    {
        // An Elite chart made only of unforced (or invisible) hat pedal notes: the
        // Elite part is active, but the downchart builder converts none of it, so the
        // downchart mask — and the implicit FourLaneDrums fallback mask — stay empty.
        var parts = ScanParts(EliteTrack(
            Note(RESOLUTION, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts.EliteDrums[Difficulty.Expert], Is.True,
                "the Elite Drums part must be active for a chart with notes");
            Assert.That(parts.EliteDrumsDownchart.IsActive(), Is.False,
                "unforced hat pedal notes must not count as a generated downchart");
            Assert.That(parts.FourLaneDrums.IsActive(), Is.False,
                "the implicit four-lane fallback mask must not be populated either");
        }
    }

    [Test]
    public void UnforcedHatPedalsOnly_RulesOfferNoTargetAndNoDifficulty()
    {
        // The rules verdict for the same degenerate chart: with no usable downchart
        // and no native drums, no target row and no difficulty may be offered — this
        // is exactly the behavior the loader backs up by building no downchart.
        var parts = ScanParts(EliteTrack(
            Note(RESOLUTION, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY)));
        var entry = EntryFromParts(parts);

        foreach (var target in Targets)
        {
            Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, target), Is.False,
                $"an elite-only song with no usable generated downchart must not be playable for {target}");
        }

        foreach (var difficulty in EnumExtensions<Difficulty>.Values)
        {
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, difficulty), Is.False,
                $"no difficulty may be offered without a usable generated downchart ({difficulty})");
        }
    }

    [Test]
    public void SnareChart_DownchartMetadataMatchesEliteDifficulties()
    {
        var parts = ScanParts(EliteTrack(
            Note(RESOLUTION, EXPERT_SNARE, MidIOHelper.VELOCITY)));

        var entry = EntryFromParts(parts);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts.EliteDrumsDownchart[Difficulty.Expert], Is.True,
                "a snare note converts, so the downchart difficulty is recorded");

            foreach (var target in Targets)
            {
                Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, target), Is.True,
                    $"a song with a usable downchart must be playable for {target}");
            }

            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Expert), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FiveLaneDrums, Difficulty.Hard), Is.False);
        }
    }

    [Test]
    public void ChannelFlaggedHatPedal_CountsAsDownchart()
    {
        // Hat pedals channel-flagged to a cymbal convert to hand gems, so they keep
        // the downchart alive — matching the loader's conversion.
        var parts = ScanParts(EliteTrack(
            Note(RESOLUTION, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY,
                MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_YELLOW)));

        Assert.That(parts.EliteDrumsDownchart[Difficulty.Expert], Is.True,
            "a channel-flagged hat pedal converts and must count as a downchart note");
    }

    [Test]
    public void GhostVelocityHatPedal_NeverCountsAsDownchart()
    {
        // Ghost-velocity hat pedals are "invisible terminators" in the full reader and
        // are dropped by the downchart builder even when channel flagged; the
        // preparser must agree so the mask never promises notes the loader removes.
        var parts = ScanParts(EliteTrack(
            Note(RESOLUTION, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY_GHOST,
                MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_GREEN)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts.EliteDrums[Difficulty.Expert], Is.True,
                "the note still exists for the Elite Drums part itself");
            Assert.That(parts.EliteDrumsDownchart.IsActive(), Is.False,
                "ghost-velocity hat pedals convert to nothing, even when channel flagged");
        }
    }

    [Test]
    public void ChordedFlaggedHatPedalWithPlainHiHat_AdvertisesOnlyViaTheHiHat()
    {
        // MAJOR regression: a channel-flagged, normal-velocity (non-strict) hat pedal
        // chorded with a hi-hat that is NOT forced-indifferent is suppressed into an
        // "invisible terminator" by MidReader.ProcessLists.SuppressNonStrictStompsAndSplashes
        // (reader ground truth: EliteDrumsNonStrictHatPedalChordedWithHiHat_... in
        // MidReaderProcessListsTests), and MoonSongLoader's downchart builder never
        // converts the pedal itself. The suppressing hi-hat always converts to a
        // yellow cymbal, though, so the difficulty stays playable and the mask must
        // keep advertising it — the scanner and the loader agree here. (A chart whose
        // ONLY notes are such pedals cannot exist: the suppression requires the
        // chorded hi-hat, which converts.) The preparser still evaluates the pedal's
        // chord context so its eligibility computation mirrors the loader per note.
        var parts = ScanParts(EliteTrack(
            Note(RESOLUTION, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY,
                MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_YELLOW),
            Note(RESOLUTION, EXPERT_HIHAT, MidIOHelper.VELOCITY)));
        var entry = EntryFromParts(parts);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts.EliteDrums[Difficulty.Expert], Is.True);
            Assert.That(parts.EliteDrumsDownchart[Difficulty.Expert], Is.True,
                "the chorded hi-hat converts to a cymbal gem, so Expert is advertised");
        }

        foreach (var target in Targets)
        {
            Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, target), Is.True,
                $"the converted hi-hat keeps the downchart usable for {target}");
        }

        Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Expert), Is.True);
    }

    [Test]
    public void UnchordedFlaggedHatPedalAndChordedSuppressedPedal_BothAdvertiseViaConvertibleNotes()
    {
        // Direct scanner/loader agreement check for the suppression: an UNFLAGGED
        // pedal chorded with a hi-hat (dropped by everyone) next to a channel-flagged
        // pedal with NO hi-hat partner (converted by everyone) must advertise via the
        // flagged pedal only — the suppressed notes never keep the mask alive.
        var parts = ScanParts(EliteTrack(
            Note(RESOLUTION, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY),
            Note(RESOLUTION, EXPERT_HIHAT, MidIOHelper.VELOCITY),
            Note(RESOLUTION * 2, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY,
                MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_GREEN)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts.EliteDrumsDownchart[Difficulty.Expert], Is.True,
                "the unchorded flagged pedal converts and keeps the difficulty alive");
            Assert.That(parts.EliteDrums[Difficulty.Expert], Is.True,
                "both chords exist for the Elite part itself");
        }
    }

    [Test]
    public void ChordedFlaggedHatPedalWithIndifferentHiHat_AdvertisesDownchart()
    {
        // The same chord, but an "indifferent hat" marker covers the hi-hat: the
        // reader leaves the hi-hat forced-indifferent, so the stomp is NOT
        // suppressed and the channel-flagged pedal converts. The mask must agree.
        var parts = ScanParts(EliteTrack(
            Note(RESOLUTION / 2, EXPERT_INDIFFERENT_HAT_MARKER, MidIOHelper.VELOCITY, 0,
                length: RESOLUTION * 2),
            Note(RESOLUTION, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY,
                MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_YELLOW),
            Note(RESOLUTION, EXPERT_HIHAT, MidIOHelper.VELOCITY)));
        var entry = EntryFromParts(parts);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts.EliteDrumsDownchart[Difficulty.Expert], Is.True,
                "a pedal chorded with a forced-indifferent hi-hat still converts, so the difficulty is advertised");

            foreach (var target in Targets)
            {
                Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, target), Is.True,
                    $"a song with a usable downchart must be playable for {target}");
            }

            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Expert), Is.True);
        }
    }

    [Test]
    public void ChordedFlaggedHatPedalAfterStrictStateText_AdvertisesDownchart()
    {
        // With [STRICT_HAT_PEDAL_STATE] active, hat pedals carry the strict flag and
        // are never suppressed by chord context, so the pedal must still advertise.
        var parts = ScanParts(EliteTrack(
            Text(0, $"[{MidIOHelper.STRICT_HAT_PEDAL_STATE}]"),
            Note(RESOLUTION, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY,
                MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_BLUE),
            Note(RESOLUTION, EXPERT_HIHAT, MidIOHelper.VELOCITY)));

        Assert.That(parts.EliteDrumsDownchart[Difficulty.Expert], Is.True,
            "strict hat pedals are never suppressed by chord context");
    }

    [Test]
    public void MixedDifficulties_SuppressedChordUnflaggedPedalAndSnare_PerDifficultyMasksMatch()
    {
        // Mixed-difficulty variant: Easy has only an unflagged pedal (converts to
        // nothing -> not advertised), Hard converts via a snare (advertised), and
        // Expert's flagged pedal is chord-suppressed but its hi-hat partner converts
        // (advertised via the hi-hat). The per-difficulty mask must match the
        // loader's per-difficulty downchart emptiness exactly.
        var parts = ScanParts(EliteTrack(
            Note(RESOLUTION, EASY_HAT_PEDAL, MidIOHelper.VELOCITY),
            Note(RESOLUTION, HARD_SNARE, MidIOHelper.VELOCITY),
            Note(RESOLUTION, EXPERT_HAT_PEDAL, MidIOHelper.VELOCITY,
                MidIOHelper.ELITE_DRUMS_CHANNEL_FLAG_YELLOW),
            Note(RESOLUTION, EXPERT_HIHAT, MidIOHelper.VELOCITY)));
        var entry = EntryFromParts(parts);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parts.EliteDrums[Difficulty.Easy], Is.True);
            Assert.That(parts.EliteDrums[Difficulty.Hard], Is.True);
            Assert.That(parts.EliteDrums[Difficulty.Expert], Is.True,
                "all three Elite difficulties stay active");
            Assert.That(parts.EliteDrumsDownchart[Difficulty.Easy], Is.False,
                "the unflagged Easy pedal converts to nothing");
            Assert.That(parts.EliteDrumsDownchart[Difficulty.Hard], Is.True,
                "the Hard snare converts, so Hard is advertised");
            Assert.That(parts.EliteDrumsDownchart[Difficulty.Expert], Is.True,
                "the suppressed Expert pedal drops, but its chorded hi-hat converts");
            Assert.That(parts.FourLaneDrums[Difficulty.Hard], Is.True,
                "the implicit four-lane fallback follows the downchart mask");
            Assert.That(parts.FourLaneDrums[Difficulty.Easy], Is.False);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, Instrument.ProDrums), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Hard), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Expert), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Easy), Is.False,
                "the difficulty with no convertible notes must not be offered");
        }
    }
}
