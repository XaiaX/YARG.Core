// pattern: Functional Core
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Common;
using NUnit.Framework;
using YARG.Core.IO;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Scanning;

public class MidiScanCompositionTests
{
    internal static FixedArray<byte> Midi(string name = "PART GUITAR", int note = 96)
    {
        var file = new MidiFile(new TrackChunk(new SequenceTrackNameEvent("SYNC")),
            new TrackChunk(new SequenceTrackNameEvent(name),
                new NoteOnEvent((SevenBitNumber)note, (SevenBitNumber)100),
                new NoteOffEvent((SevenBitNumber)note, (SevenBitNumber)0) { DeltaTime = 120 }))
            { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        using var stream = new MemoryStream();
        file.Write(stream);
        stream.Position = 0;
        return FixedArray.ReadRemainder(stream);
    }

    [Test]
    public void SupportedCompositionUsesSummariesAndMatchesRawReplacement()
    {
        using var main = Midi();
        using var update = Midi(note: 84);
        using var upgrade = Midi("PART BASS");
        var updates = new[] { update };
        var expected = AvailableParts.Default;
        var actual = AvailableParts.Default;
        var raw = MidiScanComposition.ScanRaw(main, updates, upgrade, ref expected);
        var result = MidiScanComposition.Scan(main, updates, upgrade, ref actual, out bool usedSummaries);
        Assert.That(usedSummaries, Is.True);
        Assert.That(result.Error, Is.EqualTo(raw.Error));
        Assert.That(result.Value, Is.EqualTo(480));
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void NonCanonicalChannelDataUsesRawNormalization()
    {
        using var main = Midi();
        // DWM's ReadValid policy normalizes this invalid velocity; summary compilation is not validation.
        for (int i = 0; i < main.Length - 2; i++)
            if (main[i] == 0x90 && main[i + 1] == 96) { main.Span[i + 2] = 255; break; }
        var expected = AvailableParts.Default;
        var actual = AvailableParts.Default;
        var raw = MidiScanComposition.ScanRaw(main, Array.Empty<FixedArray<byte>>(), null, ref expected);
        var result = MidiScanComposition.Scan(main, Array.Empty<FixedArray<byte>>(), null, ref actual, out bool usedSummaries);
        Assert.That(usedSummaries, Is.False);
        Assert.That(result.Error, Is.EqualTo(raw.Error));
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SourceErrorsPreserveRole(bool isUpgrade)
    {
        using var main = Midi();
        using var invalid = Midi();
        invalid.Span[12] = invalid.Span[13] = 0;
        var parts = AvailableParts.Default;
        var result = MidiScanComposition.Scan(main, isUpgrade ? Array.Empty<FixedArray<byte>>() : new[] { invalid },
            isUpgrade ? invalid : null, ref parts, out _);
        Assert.That(result.Error, Is.EqualTo(isUpgrade ? ScanResult.InvalidResolution_Upgrade : ScanResult.InvalidResolution_Update));
    }
    [TestCase(8, 0x7f)] // Unknown format
    [TestCase(11, 1)] // Header claims fewer tracks
    [TestCase(11, 3)] // Header claims more tracks
    [TestCase(12, 0xe7)] // SMPTE time division
    [TestCase(24, 0x7f)] // Sequencer-specific metadata instead of SYNC name
    public void NoncanonicalSourcesPreserveRawResultsOrExceptions(int index, int value)
    {
        using var midi = Midi();
        midi.Span[index] = (byte)value;
        var expected = AvailableParts.Default;
        var actual = AvailableParts.Default;
        var raw = default(ScanExpected<long>);
        var result = default(ScanExpected<long>);
        bool usedSummaries = true;
        Exception? rawError = null;
        Exception? actualError = null;
        try { raw = MidiScanComposition.ScanRaw(midi, Array.Empty<FixedArray<byte>>(), null, ref expected); }
        catch (Exception error) { rawError = error; }
        try { result = MidiScanComposition.Scan(midi, Array.Empty<FixedArray<byte>>(), null, ref actual, out usedSummaries); }
        catch (Exception error) { actualError = error; }
        Assert.That(usedSummaries, Is.False);
        Assert.That(actualError?.GetType(), Is.EqualTo(rawError?.GetType()));
        Assert.That(result.Error, Is.EqualTo(raw.Error));
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void EarlierUpdateErrorWinsOverMalformedLaterSource()
    {
        using var main = Midi();
        using var invalid = Midi();
        invalid.Span[12] = invalid.Span[13] = 0;
        main.Span[14] = 0; // Invalid MTrk tag must never override the earlier update error.
        var parts = AvailableParts.Default;
        var result = MidiScanComposition.Scan(main, new[] { invalid }, null, ref parts, out bool usedSummaries);
        Assert.That(usedSummaries, Is.False);
        Assert.That(result.Error, Is.EqualTo(ScanResult.InvalidResolution_Update));
        Assert.That(parts, Is.EqualTo(AvailableParts.Default));
    }

    [Test]
    public void StructuralGateRejectsTruncatedTracksAndMissingEndWithoutUnsafeReads()
    {
        using var midi = Midi();
        Assert.That(MidiSummaryEligibility.IsSupported(midi.ReadOnlySpan), Is.True);
        for (int length = 0; length < midi.Length; length++)
            Assert.That(MidiSummaryEligibility.IsSupported(midi.ReadOnlySpan.Slice(0, length)), Is.False, $"length {length}");
        midi.Span[midi.Length - 2] = 1; // Final event is text, not EOT.
        Assert.That(MidiSummaryEligibility.IsSupported(midi.ReadOnlySpan), Is.False);
    }

}
