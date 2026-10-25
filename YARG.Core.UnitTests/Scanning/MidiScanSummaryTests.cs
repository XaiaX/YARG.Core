// pattern: Functional Core
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Common;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Extensions;
using YARG.Core.IO;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Scanning;

public class MidiScanSummaryTests
{
    private static TrackChunk Track(string name, params int[] notes)
    {
        var track = new TrackChunk(new SequenceTrackNameEvent(name));
        foreach (int note in notes)
        {
            track.Events.Add(new NoteOnEvent((SevenBitNumber) note, (SevenBitNumber) 100));
            track.Events.Add(new NoteOffEvent((SevenBitNumber) note, (SevenBitNumber) 0) { DeltaTime = 120 });
        }
        return track;
    }

    private static MidiFile File(params TrackChunk[] tracks)
    {
        var file = new MidiFile(tracks) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        using var bytes = Bytes(file);
        using var stream = bytes.ToReferenceStream();
        return MidiFile.Read(stream, MidiSettingsLatin1.Instance);
    }

    private static FixedArray<byte> Bytes(MidiFile file)
    {
        using var stream = new MemoryStream();
        file.Write(stream);
        stream.Position = 0;
        return FixedArray.ReadRemainder(stream);
    }

    private static MidiScanSummary Compile(MidiFile file)
    {
        using var bytes = Bytes(file);
        return MidiScanSummary.Compile(bytes);
    }

    private static void AssertParity(MidiFile file, MidiScanSummary summary)
    {
        for (int context = 0; context < 8; context++)
        {
            var expectedParts = AvailableParts.Default;
            var actualParts = AvailableParts.Default;
            var expectedDrums = (DrumsType) context;
            var actualDrums = expectedDrums;
            using var bytes = Bytes(file);
            var expected = SongEntry.ParseMidi(bytes, ref expectedParts, ref expectedDrums);
            var actual = summary.Scan(ref actualParts, ref actualDrums);
            Assert.That(actual.ToString(), Is.EqualTo(expected.HasValue ? "Success" : expected.Error == ScanResult.InvalidResolution ? "InvalidResolution" : "MultipleTrackNames"));
            Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(actualParts), Is.EqualTo(Newtonsoft.Json.JsonConvert.SerializeObject(expectedParts)), $"context {context}: {string.Join(",", file.GetTrackChunks().Select(t => t.GetTrackName()))}");
            Assert.That(actualDrums, Is.EqualTo(expectedDrums), $"context {context}");
            if (expected.HasValue) Assert.That(summary.Resolution, Is.EqualTo(expected.Value));
        }
    }

    [Test]
    public void EveryRecognizedInstrumentAndDifficultyMatchesScanner()
    {
        foreach (string name in YARGMidiTrack.TRACKNAMES.Keys)
        {
            var file = File(Track("SYNC"), Track(name, Enumerable.Range(0, 128).ToArray()));
            AssertParity(file, Compile(file));
        }
    }

    [Test]
    public void OrderedMergePreservesEmptyReplacementDuplicatesAndSyncTrack()
    {
        var file = File(Track("PART BASS", 96), Track("PART GUITAR", 96), Track("PART GUITAR", 84), Track("VENUE"));
        var update = File(Track("PART BASS", 60), Track("PART GUITAR"), Track("", 96), Track("VENUE", 96));
        var upgrade = File(Track("SYNC"), Track("PART GUITAR", 72), Track("PART GUITAR", 60));
        var summary = Compile(file).Merge(Compile(update)).Merge(Compile(upgrade));
        file.Merge(update); file.Merge(upgrade);
        AssertParity(file, summary);
        Assert.That(summary.TrackCount, Is.EqualTo(file.Chunks.Count));
    }

    [Test]
    public void EmptyDrumTrackStateAndNativeElitePrecedenceMatch()
    {
        var file = File(Track("SYNC"), Track("PART DRUMS", 110), Track("PART ELITE_DRUMS", 60, 72, 84, 96), Track("PART DRUMS", 101, 96));
        AssertParity(file, Compile(file));
    }

    [Test]
    public void HarmonyEligibilityComposesAcrossFiles()
    {
        var file = File(Track("SYNC"), Track("HARM2", 60), Track("HARM3", 60));
        AssertParity(file, Compile(file));
        var lead = Track("HARM1");
        lead.Events.Add(new NoteOnEvent((SevenBitNumber)105, (SevenBitNumber)100));
        lead.Events.Add(new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)100));
        lead.Events.Add(new NoteOffEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 120 });
        var update = File(Track("SYNC"), lead);
        var summary = Compile(file).Merge(Compile(update));
        file.Merge(update);
        AssertParity(file, summary);
    }

    [Test]
    public void DeterministicGeneratedLayerCombinationsMatchIndependentMergedScanner()
    {
        var random = new Random(417);
        string[] names = { "SYNC", "PART GUITAR", "T1 GEMS", "PART DRUMS", "PART ELITE_DRUMS", "PART BASS", "HARM2", "VENUE", "" };
        for (int sample = 0; sample < 100; sample++)
        {
            MidiFile Generate() => File(Enumerable.Range(0, random.Next(1, 8)).Select(_ => Track(names[random.Next(names.Length)], Enumerable.Range(0, random.Next(0, 8)).Select(_ => random.Next(50, 113)).ToArray())).ToArray());
            var file = Generate();
            var summary = Compile(file);
            for (int layer = 0; layer < 3; layer++)
            {
                var update = Generate();
                summary = summary.Merge(Compile(update));
                file.Merge(update);
                AssertParity(file, summary);
            }
        }
    }

    [Test]
    public void ConflictingTrackNamesAndZeroResolutionMatch()
    {
        var track = Track("PART GUITAR", 96);
        track.Events.Insert(1, new SequenceTrackNameEvent("PART BASS"));
        var file = File(Track("SYNC"), track);
        AssertParity(file, Compile(file));
        using var bytes = Bytes(File(Track("SYNC"), Track("PART GUITAR", 96)));
        bytes.Span[12] = bytes.Span[13] = 0;
        var summary = MidiScanSummary.Compile(bytes);
        var parts = AvailableParts.Default;
        var drums = DrumsType.Any;
        Assert.That(summary.Scan(ref parts, ref drums), Is.EqualTo(MidiSummaryScanResult.InvalidResolution));
    }

    [Test]
    public void MalformedUnselectedDuplicateIsDeferredAndSelectedFailureRequestsFallback()
    {
        using var bytes = Bytes(File(Track("SYNC"), Track("PART GUITAR", 96), Track("PART GUITAR", 84)));
        // Valid name and initial nonzero-tick event, followed by an overlong meta event.
        bytes.Span[bytes.Length - 2] = 0x7f;
        bytes.Span[bytes.Length - 1] = 0x7f;
        var summary = MidiScanSummary.Compile(bytes);
        var expectedParts = AvailableParts.Default;
        var actualParts = AvailableParts.Default;
        var expectedDrums = DrumsType.Any;
        var actualDrums = expectedDrums;
        Assert.That(SongEntry.ParseMidi(bytes, ref expectedParts, ref expectedDrums).HasValue, Is.True);
        Assert.That(summary.Scan(ref actualParts, ref actualDrums), Is.EqualTo(MidiSummaryScanResult.Success));
        Assert.That(actualParts, Is.EqualTo(expectedParts));

        using var selected = Bytes(File(Track("SYNC"), Track("PART GUITAR", 96)));
        selected.Span[selected.Length - 2] = 0x7f;
        selected.Span[selected.Length - 1] = 0x7f;
        summary = MidiScanSummary.Compile(selected);
        actualParts = AvailableParts.Default;
        Assert.That(summary.Scan(ref actualParts, ref actualDrums), Is.EqualTo(MidiSummaryScanResult.Unsupported));
        Assert.That(actualParts, Is.EqualTo(AvailableParts.Default));
    }

    [Test]
    public void ZeroResolutionDoesNotReadInvalidTrackBody()
    {
        using var bytes = Bytes(File(Track("SYNC"), Track("PART GUITAR", 96)));
        bytes.Span[12] = bytes.Span[13] = 0;
        bytes.Span[14] = 0;
        var summary = MidiScanSummary.Compile(bytes);
        var parts = AvailableParts.Default;
        var drums = DrumsType.Any;
        Assert.That(summary.Scan(ref parts, ref drums), Is.EqualTo(MidiSummaryScanResult.InvalidResolution));
    }

    [Test]
    public void ExistingPartsArePreservedAndNativeDrumObservationsStillRefresh()
    {
        var file = File(Track("SYNC"), Track("PART GUITAR", 60), Track("PART DRUMS", 96), Track("PART ELITE_DRUMS", 76));
        var summary = Compile(file);
        for (int context = 0; context < 8; context++)
        {
            var expectedParts = AvailableParts.Default;
            expectedParts.FiveFretGuitar.Difficulties = DifficultyMask.Expert;
            expectedParts.EliteDrums.Difficulties = DifficultyMask.Hard;
            expectedParts.EliteDrumsDownchart.Difficulties = DifficultyMask.Easy;
            expectedParts.HarmonyVocals.ActivateSubtrack(0);
            var actualParts = expectedParts;
            var expectedDrums = (DrumsType)context;
            var actualDrums = expectedDrums;
            using var bytes = Bytes(file);
            SongEntry.ParseMidi(bytes, ref expectedParts, ref expectedDrums);
            Assert.That(summary.Scan(ref actualParts, ref actualDrums), Is.EqualTo(MidiSummaryScanResult.Success));
            Assert.That(actualParts, Is.EqualTo(expectedParts));
            Assert.That(actualDrums, Is.EqualTo(expectedDrums));
        }
    }

    [TestCase(false, false, 100)]
    [TestCase(true, false, 100)]
    [TestCase(false, true, 100)]
    [TestCase(true, true, 1)]
    public void ElitePedalChordMarkersAndChannelsMatch(bool strict, bool indifferent, int velocity)
    {
        var track = Track("PART ELITE_DRUMS");
        if (strict) track.Events.Add(new Melanchall.DryWetMidi.Core.TextEvent("STRICT_HAT_PEDAL_STATE"));
        if (indifferent) track.Events.Add(new NoteOnEvent((SevenBitNumber)88, (SevenBitNumber)100));
        track.Events.Add(new NoteOnEvent((SevenBitNumber)72, (SevenBitNumber)velocity) { Channel = (FourBitNumber)11 });
        track.Events.Add(new NoteOnEvent((SevenBitNumber)76, (SevenBitNumber)100));
        track.Events.Add(new NoteOffEvent((SevenBitNumber)72, (SevenBitNumber)0) { Channel = (FourBitNumber)11, DeltaTime = 120 });
        track.Events.Add(new NoteOffEvent((SevenBitNumber)76, (SevenBitNumber)0));
        if (indifferent) track.Events.Add(new NoteOffEvent((SevenBitNumber)88, (SevenBitNumber)0));
        var file = File(Track("SYNC"), track);
        AssertParity(file, Compile(file));
    }

    [Test]
    public void VelocityZeroAndLatin1NamesMatchRawAndNormalizedMerge()
    {
        var track = Track("PART GUITAR");
        track.Events.Add(new NoteOnEvent((SevenBitNumber)96, (SevenBitNumber)100));
        track.Events.Add(new NoteOnEvent((SevenBitNumber)96, (SevenBitNumber)0) { DeltaTime = 120 });
        var file = File(Track("SYNC"), track, Track("VÉNUE"));
        var update = File(Track("SYNC"), Track("VÉNUE", 96));
        var summary = Compile(file).Merge(Compile(update));
        file.Merge(update);
        AssertParity(file, summary);
        Assert.That(summary.TrackCount, Is.EqualTo(file.Chunks.Count));
    }

    [Test]
    public void LocalMidiCorpusParity()
    {
        var path = Environment.GetEnvironmentVariable("YARG_SUMMARY_MIDI_CORPUS");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set YARG_SUMMARY_MIDI_CORPUS for local differential verification.");
        int count = 0;
        foreach (var filename in Directory.EnumerateFiles(path!, "*.mid", SearchOption.AllDirectories).Where(p => !p.Contains("_archive")))
        {
            using var bytes = FixedArray.LoadFile(filename);
            var summary = MidiScanSummary.Compile(bytes);
            for (int context = 0; context < 8; context++)
            {
                var expectedParts = AvailableParts.Default;
                var actualParts = AvailableParts.Default;
                var expectedDrums = (DrumsType)context;
                var actualDrums = expectedDrums;
                var expected = SongEntry.ParseMidi(bytes, ref expectedParts, ref expectedDrums);
                var actual = summary.Scan(ref actualParts, ref actualDrums);
                Assert.That(actual == MidiSummaryScanResult.Success, Is.EqualTo(expected.HasValue), filename);
                Assert.That(actualParts, Is.EqualTo(expectedParts), filename);
                Assert.That(actualDrums, Is.EqualTo(expectedDrums), filename);
            }
            using var stream = bytes.ToReferenceStream();
            var file = MidiFile.Read(stream, MidiSettingsLatin1.Instance);
            AssertParity(file, summary);
            count++;
        }
        Assert.That(count, Is.GreaterThan(0));
        TestContext.Out.WriteLine($"Compared {count} real MIDI files in all eight drum contexts.");
    }
}
