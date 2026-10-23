using System;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Common;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Song;
using YARG.Core.Extensions;
using YARG.Core.IO;
using ChartFormat = YARG.Core.Song.ChartFormat;

namespace YARG.Core.UnitTests.Song;

[NonParallelizable] // Scan progress and full-load observation are process-global.
public sealed class AuthoredDrumScanTests
{
    [TestCase(ChartFormat.Mid, false)]
    [TestCase(ChartFormat.Midi, true)]
    public void MidiPresence_PreservesBothNativeTracksRegardlessOfOrder(ChartFormat format, bool eliteFirst)
    {
        WithDirectory(root =>
        {
            var classic = Track("PART DRUMS", 84); // Hard native, Expert Elite.
            var elite = Track("PART ELITE_DRUMS", 75, 73);
            var midi = new MidiFile(new TrackChunk()) { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) };
            midi.Chunks.Add(eliteFirst ? elite : classic);
            midi.Chunks.Add(eliteFirst ? classic : elite);
            string path = Path.Combine(root, format == ChartFormat.Mid ? "notes.mid" : "notes.midi");
            midi.Write(path);
            using var observation = FullLoadObservation.Begin();
            var entry = Scan(root, path, format);
            Assert.That(entry.DrumSourcePresence.Has(DrumSourceFormat.FourLane, Difficulty.Hard), Is.True);
            Assert.That(entry.DrumSourcePresence.Has(DrumSourceFormat.FourLane, Difficulty.Expert), Is.False);
            Assert.That(entry.DrumSourcePresence.Has(DrumSourceFormat.Elite, Difficulty.Expert), Is.True);
            Assert.That(entry.DrumSourcePresence.FiveLane, Is.EqualTo(DifficultyMask.None));
            AssertNoFullLoads(observation);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChartPresence_ClassifiesNativeLaneDomain(bool fiveLane)
    {
        WithDirectory(root =>
        {
            string path = Path.Combine(root, "notes.chart");
            File.WriteAllText(path, "[Song]\n{\n  Resolution = 192\n}\n[SyncTrack]\n{\n  0 = B 120000\n}\n[ExpertDrums]\n{\n  0 = N 0 0\n  192 = N " + (fiveLane ? "5" : "1") + " 0\n  9600 = N 32 0\n}\n");
            using var observation = FullLoadObservation.Begin();
            var entry = Scan(root, path, ChartFormat.Chart, fiveLane);
            Assert.That(entry.DrumSourcePresence.Has(fiveLane ? DrumSourceFormat.FiveLane : DrumSourceFormat.FourLane,
                Difficulty.Expert), Is.True);
            Assert.That(entry.DrumSourcePresence.Has(fiveLane ? DrumSourceFormat.FourLane : DrumSourceFormat.FiveLane,
                Difficulty.Expert), Is.False);
            Assert.That(entry.DrumSourcePresence.Elite, Is.EqualTo(DifficultyMask.None));
            AssertNoFullLoads(observation);
        });
    }

    [Test]
    public void GeneratedEliteDownchart_IsNotNativeClassicPresence()
    {
        WithDirectory(root =>
        {
            string path = Path.Combine(root, "notes.mid");
            new MidiFile(new TrackChunk(), Track("PART ELITE_DRUMS", 75))
                { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) }.Write(path);
            using var observation = FullLoadObservation.Begin();
            var entry = Scan(root, path, ChartFormat.Mid);
            Assert.That(entry[Instrument.ProDrums][Difficulty.Expert], Is.True);
            Assert.That(entry.DrumSourcePresence.FourLane, Is.EqualTo(DifficultyMask.None));
            Assert.That(entry.DrumSourcePresence.FiveLane, Is.EqualTo(DifficultyMask.None));
            Assert.That(entry.DrumSourcePresence.Has(DrumSourceFormat.Elite, Difficulty.Expert, true), Is.True);
            Assert.That(DrumCandidateResolver.Resolve(entry.DrumSourcePresence, Instrument.ProDrums,
                Difficulty.Expert, false, false)!.Value.SourceFormat, Is.EqualTo(DrumSourceFormat.Elite));
            AssertNoFullLoads(observation);
        });
    }

    [Test]
    public void EliteGeneratedAvailability_DoesNotSuppressNativeLaterTier()
    {
        var midi = new MidiFile(new TrackChunk(), Track("PART ELITE_DRUMS", 75), Track("PART DRUMS", 84))
            { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) };
        using var stream = new MemoryStream();
        midi.Write(stream);
        using var bytes = FixedArray<byte>.Alloc((int) stream.Length);
        stream.ToArray().CopyTo(bytes.Span);
        var parts = TestSongEntry.ParseMidiForTest(bytes);
        Assert.That(parts.FourLaneDrums[Difficulty.Hard], Is.True);
        Assert.That(parts.FourLaneDrums[Difficulty.Expert], Is.False);
        Assert.That(parts.NativeFourLaneDrums, Is.EqualTo(DifficultyMask.Hard));
        Assert.That(parts.NativeEliteDrums, Is.EqualTo(DifficultyMask.Expert));
    }

    [TestCase("notes.mid")]
    [TestCase("notes.chart")]
    public void SngPresence_QuickFullAndOldCacheScansNeverLoadFullCharts(string chartName)
    {
        WithDirectory(root =>
        {
            byte[] payload;
            if (chartName.EndsWith("mid"))
            {
                using var midiStream = new MemoryStream();
                new MidiFile(new TrackChunk(), Track("PART DRUMS", 96, 95))
                    { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) }.Write(midiStream);
                payload = midiStream.ToArray();
            }
            else
            {
                payload = System.Text.Encoding.UTF8.GetBytes("[Song]\n{\n  Resolution = 192\n}\n[SyncTrack]\n{\n  0 = B 120000\n}\n[ExpertDrums]\n{\n  0 = N 0 0\n  192 = N 1 0\n}\n");
            }
            SngScanFixture.Write(Path.Combine(root, "fixture.sng"), chartName, payload);
            string cache = Path.Combine(root, "cache.bin");
            SongEntry Run(bool quick) => YARG.Core.Song.Cache.CacheHandler.RunScan(quick, cache,
                Path.Combine(root, "bad.txt"), false, new System.Collections.Generic.List<string> { root })
                .Entries.Values.SelectMany(e => e).Single();
            using var observation = FullLoadObservation.Begin();
            var presence = Run(false).DrumSourcePresence;
            Assert.That(presence.Has(DrumSourceFormat.FourLane, Difficulty.Expert), Is.True);
            Assert.That(Run(true).DrumSourcePresence, Is.EqualTo(presence));
            Assert.That(Run(false).DrumSourcePresence, Is.EqualTo(presence));
            foreach (int version in new[] { 26_09_25_01, 26_09_25_00 })
            {
                using (var old = File.OpenWrite(cache)) old.Write(version, Endianness.Little);
                Assert.That(Run(true).DrumSourcePresence, Is.EqualTo(presence), "Old caches must trigger a rescan.");
                using var current = new BinaryReader(File.OpenRead(cache));
                Assert.That(current.ReadInt32(), Is.EqualTo(26_10_19_01));
            }
            AssertNoFullLoads(observation);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CorruptCurrentCache_LateGroupFailureRebuildsCompleteFreshLibrary(bool truncate)
    {
        WithDirectory(root =>
        {
            void AddSong(string folder, string title)
            {
                string directory = Path.Combine(root, folder);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "notes.mid");
                new MidiFile(new TrackChunk(), Track("PART DRUMS", 96))
                    { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) }.Write(path);
                File.WriteAllText(Path.Combine(directory, "song.ini"),
                    "[song]\nname = " + title + "\nsong_length = 60000\n");
                File.WriteAllBytes(Path.Combine(directory, "song.opus"), Array.Empty<byte>());
            }
            string cache = Path.Combine(root, "cache.bin");
            SongEntry[] Run(bool quick) => YARG.Core.Song.Cache.CacheHandler.RunScan(quick, cache,
                Path.Combine(root, "bad.txt"), false, new System.Collections.Generic.List<string> { root })
                .Entries.Values.SelectMany(group => group).ToArray();
            AddSong("first", "Original");
            Assert.That(Run(false), Has.Length.EqualTo(1));
            byte[] bytes = File.ReadAllBytes(cache);
            Assert.That(BitConverter.ToInt32(bytes, 0), Is.EqualTo(26_10_19_01));
            // The final section is the empty CON-group count, read after valid INI entries.
            Assert.That(BitConverter.ToInt32(bytes, bytes.Length - sizeof(int)), Is.Zero);
            if (truncate)
                Array.Resize(ref bytes, bytes.Length - 1);
            else
                BitConverter.GetBytes(1).CopyTo(bytes, bytes.Length - sizeof(int));
            File.WriteAllBytes(cache, bytes);
            File.WriteAllText(Path.Combine(root, "first", "song.ini"),
                "[song]\nname = Fresh\nsong_length = 60000\n");
            AddSong("second", "Added");
            using var observation = FullLoadObservation.Begin();
            foreach (bool quick in new[] { true, true, false })
            {
                var recovered = Run(quick);
                Assert.That(recovered.Select(entry => entry.Name.ToString()),
                    Is.EquivalentTo(new[] { "Fresh", "Added" }));
                Assert.That(recovered.All(entry => entry.DrumSourcePresence.FourLane == DifficultyMask.Expert), Is.True);
            }
            AssertNoFullLoads(observation);
        });
    }

    [Test]
    public void SelectedSong_StillLoadsAuthoredSources()
    {
        WithDirectory(root =>
        {
            string path = Path.Combine(root, "notes.mid");
            new MidiFile(new TrackChunk(), Track("PART DRUMS", 96, 95), Track("PART ELITE_DRUMS", 75))
                { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) }.Write(path);
            var entry = Scan(root, path, ChartFormat.Mid);
            using var observation = FullLoadObservation.Begin();
            var chart = entry.LoadChart();
            Assert.That(chart, Is.Not.Null);
            Assert.That(chart!.AuthoredDrumSources.Tiers.Keys.Select(key => key.Item1),
                Does.Contain(DrumSourceFormat.FourLane).And.Contain(DrumSourceFormat.Elite));
            Assert.That(observation.MidiLoads, Is.GreaterThan(0));
            Assert.That(observation.AuthoredNoteBatches, Is.GreaterThan(0));
            Assert.That(observation.AuthoredNotes, Is.GreaterThan(0));
        });
    }

    [Test]
    public void FiveLaneMidiPresence_DoesNotBecomeNativeFourLane()
    {
        WithDirectory(root =>
        {
            string path = Path.Combine(root, "notes.mid");
            new MidiFile(new TrackChunk(), Track("PART DRUMS", 96, 101))
                { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) }.Write(path);
            using var observation = FullLoadObservation.Begin();
            var entry = Scan(root, path, ChartFormat.Mid, true);
            Assert.That(entry.DrumSourcePresence.FiveLane, Is.EqualTo(DifficultyMask.Expert));
            Assert.That(entry.DrumSourcePresence.FourLane, Is.EqualTo(DifficultyMask.None));
            AssertNoFullLoads(observation);
        });
    }

    private static void AssertNoFullLoads(FullLoadObservation observation)
    {
        Assert.That(observation.MidiLoads, Is.Zero);
        Assert.That(observation.DotChartLoads, Is.Zero);
        Assert.That(observation.AuthoredNoteBatches, Is.Zero);
        Assert.That(observation.AuthoredNotes, Is.Zero);
    }

    private static TrackChunk Track(string name, params int[] pitches)
    {
        var track = new TrackChunk(new SequenceTrackNameEvent(name));
        foreach (int pitch in pitches)
        {
            track.Events.Add(new NoteOnEvent((SevenBitNumber) pitch, (SevenBitNumber) 100) { DeltaTime = 9600 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber) pitch, (SevenBitNumber) 0) { DeltaTime = 96 });
        }
        return track;
    }

    private static SongEntry Scan(string root, string path, ChartFormat format, bool fiveLane = false)
    {
        string ini = Path.Combine(root, "song.ini");
        File.WriteAllText(ini, "[song]\nname = Presence fixture\nsong_length = 60000\nfive_lane_drums = " + fiveLane + "\n");
        var result = UnpackedIniEntry.ProcessNewEntry(root, new FileInfo(path), format, new FileInfo(ini), "Tests");
        Assert.That(result.HasValue, Is.True, result.Error.ToString());
        return result.Value;
    }

    private static void WithDirectory(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "yarg-presence-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, true); }
    }
}
