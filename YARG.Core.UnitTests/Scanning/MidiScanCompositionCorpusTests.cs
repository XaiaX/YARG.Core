// pattern: Imperative Shell
using System.Diagnostics;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.IO;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.Scanning;

public sealed class MidiScanCompositionCorpusTests
{
    [Test]
    public void LocalUpdateCorpusMatchesLegacyComposition()
    {
        string? root = Environment.GetEnvironmentVariable("YARG_SUMMARY_MIDI_CORPUS");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("Set YARG_SUMMARY_MIDI_CORPUS for local composition parity.");
        using var baseMidi = BaseMidi();
        int count = 0, summaries = 0;
        long rawTicks = 0, summaryTicks = 0;
        foreach (string filename in Directory.EnumerateFiles(root!, "*.mid", SearchOption.AllDirectories)
            .Where(p => !p.Contains("_archive")).OrderBy(p => p, StringComparer.Ordinal))
        {
            using var supplement = FixedArray.LoadFile(filename);
            var updates = new[] { supplement };
            var expectedParts = AvailableParts.Default;
            var actualParts = AvailableParts.Default;
            long start = Stopwatch.GetTimestamp();
            var expected = MidiScanComposition.ScanRaw(baseMidi, updates, null, ref expectedParts);
            rawTicks += Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();
            var actual = MidiScanComposition.Scan(baseMidi, updates, null, ref actualParts, out bool usedSummaries);
            summaryTicks += Stopwatch.GetTimestamp() - start;
            Assert.That(actual.Error, Is.EqualTo(expected.Error), filename);
            if (expected.HasValue)
            {
                Assert.That(actual.Value, Is.EqualTo(expected.Value), filename);
                Assert.That(actualParts, Is.EqualTo(expectedParts), filename);
            }
            if (usedSummaries) summaries++;
            count++;
        }
        Assert.That(count, Is.GreaterThan(0));
        Assert.That(summaries, Is.GreaterThan(0), "The corpus must exercise the summary path, not only fallback.");
        TestContext.Out.WriteLine($"Compositions: {count}; summaries: {summaries}; fallback: {count - summaries}; " +
            $"legacy seconds: {(double)rawTicks / Stopwatch.Frequency:F3}; integrated seconds: {(double)summaryTicks / Stopwatch.Frequency:F3}.");
    }

    private static FixedArray<byte> BaseMidi()
    {
        var midi = new MidiFile(new TrackChunk(new SequenceTrackNameEvent("SYNC")),
            new TrackChunk(new SequenceTrackNameEvent("PART BASS"),
                new NoteOnEvent((SevenBitNumber)96, (SevenBitNumber)100),
                new NoteOffEvent((SevenBitNumber)96, (SevenBitNumber)0) { DeltaTime = 120 }))
        { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        using var stream = new MemoryStream();
        midi.Write(stream);
        stream.Position = 0;
        return FixedArray.ReadRemainder(stream);
    }
}
