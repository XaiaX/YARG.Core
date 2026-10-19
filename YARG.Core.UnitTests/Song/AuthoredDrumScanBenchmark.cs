using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Song.Cache;

namespace YARG.Core.UnitTests.Song;

[NonParallelizable]
public sealed class AuthoredDrumScanBenchmark
{
    // Cold means no song cache, not an OS filesystem-cache flush. No timing threshold is asserted.
    [Test, Explicit("Opt-in temporary-library scan benchmark"), Category("ScanBenchmark")]
    public void ColdFullQuickTemporaryLibrary()
    {
        const int repetitions = 3;
        const int copies = 8;
        const int notesPerTrack = 256;
        for (int repetition = 1; repetition <= repetitions; repetition++)
        {
            string root = Path.Combine(Path.GetTempPath(), "yarg-scan-benchmark-" + Guid.NewGuid().ToString("N"));
            string library = Path.Combine(root, "songs");
            Directory.CreateDirectory(library);
            try
            {
                byte[] midi = CreateMidi(notesPerTrack);
                var chart = new StringBuilder("[Song]\n{\n Resolution = 192\n}\n[SyncTrack]\n{\n 0 = B 120000\n}\n[ExpertDrums]\n{\n");
                for (int note = 0; note < notesPerTrack; note++)
                    chart.AppendLine($" {note * 48} = N {(note % 3 == 0 ? 32 : note % 2)} 0");
                chart.AppendLine("}");
                byte[] chartBytes = Encoding.UTF8.GetBytes(chart.ToString());
                for (int copy = 0; copy < copies; copy++)
                {
                    WriteUnpacked(library, $"mid-{copy}", "notes.mid", midi);
                    WriteUnpacked(library, $"midi-{copy}", "notes.midi", midi);
                    WriteUnpacked(library, $"chart-{copy}", "notes.chart", chartBytes);
                    SngScanFixture.Write(Path.Combine(library, $"midi-{copy}.sng"), "notes.mid", midi);
                    SngScanFixture.Write(Path.Combine(library, $"chart-{copy}.sng"), "notes.chart", chartBytes);
                }
                string cache = Path.Combine(root, "cache.bin");
                foreach (string phase in new[] { "cold", "full", "quick" })
                {
                    using var observation = FullLoadObservation.Begin();
                    var timer = Stopwatch.StartNew();
                    var result = CacheHandler.RunScan(phase == "quick", cache, Path.Combine(root, "bad.txt"),
                        false, new List<string> { library });
                    timer.Stop();
                    // Identical chart hashes may share a dictionary key: count entries, not keys.
                    var entries = result.Entries.Values.SelectMany(group => group).ToArray();
                    TestContext.Progress.WriteLine($"SCAN_BASELINE repetition={repetition} phase={phase} " +
                        $"elapsed_ms={timer.Elapsed.TotalMilliseconds:F3} entries={entries.Length} " +
                        $"midi_loads={observation.MidiLoads} chart_loads={observation.DotChartLoads} " +
                        $"authored_batches={observation.AuthoredNoteBatches} authored_notes={observation.AuthoredNotes}");
                    Assert.That(entries.Length, Is.EqualTo(copies * 5));
                    Assert.That(observation.MidiLoads, Is.Zero);
                    Assert.That(observation.DotChartLoads, Is.Zero);
                    Assert.That(observation.AuthoredNoteBatches, Is.Zero);
                    Assert.That(observation.AuthoredNotes, Is.Zero);
                    Assert.That(entries.All(entry => entry.DrumSourcePresence.FourLane != DifficultyMask.None ||
                        entry.DrumSourcePresence.FiveLane != DifficultyMask.None ||
                        entry.DrumSourcePresence.Elite != DifficultyMask.None), Is.True);
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }

    [Test]
    public void Observation_RejectsOverlappingScopesAndCanBeReused()
    {
        using (FullLoadObservation.Begin())
            Assert.Throws<InvalidOperationException>(() => FullLoadObservation.Begin());
        using var next = FullLoadObservation.Begin();
        Assert.That(next.MidiLoads + next.DotChartLoads + next.AuthoredNotes, Is.Zero);
    }

    private static void WriteUnpacked(string library, string name, string chartName, byte[] payload)
    {
        string directory = Path.Combine(library, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "song.ini"), "[song]\nname = Scan benchmark\nsong_length = 60000\n");
        File.WriteAllBytes(Path.Combine(directory, chartName), payload);
        File.WriteAllBytes(Path.Combine(directory, "song.opus"), Array.Empty<byte>());
    }

    private static byte[] CreateMidi(int noteCount)
    {
        var midi = new MidiFile(new TrackChunk()) { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) };
        foreach (string name in new[] { "PART DRUMS", "PART ELITE_DRUMS" })
        {
            var track = new TrackChunk(new SequenceTrackNameEvent(name));
            for (int note = 0; note < noteCount; note++)
            {
                int pitch = name == "PART DRUMS" ? new[] { 96, 95, 97 }[note % 3] : new[] { 74, 73, 75 }[note % 3];
                track.Events.Add(new NoteOnEvent((SevenBitNumber) pitch, (SevenBitNumber) 100) { DeltaTime = 24 });
                track.Events.Add(new NoteOffEvent((SevenBitNumber) pitch, (SevenBitNumber) 0) { DeltaTime = 24 });
            }
            midi.Chunks.Add(track);
        }
        using var stream = new MemoryStream();
        midi.Write(stream);
        return stream.ToArray();
    }
}
