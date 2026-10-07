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

[NonParallelizable] // CacheHandler scan progress and audio logging are process-global.
public sealed class AuthoredDrumScanTests
{
    [TestCase(ChartFormat.Mid, false)]
    [TestCase(ChartFormat.Midi, true)]
    public void MidiFacts_ReadLateExtraKickAndBothNativeTracksRegardlessOfOrder(ChartFormat format, bool eliteFirst)
    {
        WithDirectory(root =>
        {
            var classic = Track("PART DRUMS", 96, 95);
            var elite = Track("PART ELITE_DRUMS", 75, 73);
            var midi = new MidiFile(new TrackChunk()) { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) };
            midi.Chunks.Add(eliteFirst ? elite : classic);
            midi.Chunks.Add(eliteFirst ? classic : elite);
            string path = Path.Combine(root, format == ChartFormat.Mid ? "notes.mid" : "notes.midi");
            midi.Write(path);
            var entry = Scan(root, path, format);
            Assert.That(entry.AuthoredDrumSourceFacts.Select(f => f.Source),
                Is.EquivalentTo(new[] { DrumSourceFormat.FourLane, DrumSourceFormat.Elite }));
            var facts = entry.AuthoredDrumSourceFacts.Single(f => f.Source == DrumSourceFormat.FourLane);
            Assert.That(facts.OrdinaryKick && facts.ExtraKick, Is.True);
            Assert.That(entry[Instrument.ProDrums][Difficulty.Expert], Is.True);
            AssertMatchesLoaded(entry);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChartFacts_ReadLateNote32AndClassifyNativeLaneDomain(bool fiveLane)
    {
        WithDirectory(root =>
        {
            string path = Path.Combine(root, "notes.chart");
            File.WriteAllText(path, "[Song]\n{\n  Resolution = 192\n}\n[SyncTrack]\n{\n  0 = B 120000\n}\n[ExpertDrums]\n{\n  0 = N 0 0\n  192 = N " + (fiveLane ? "5" : "1") + " 0\n  9600 = N 32 0\n}\n");
            var entry = Scan(root, path, ChartFormat.Chart, fiveLane);
            var facts = entry.AuthoredDrumSourceFacts.Single();
            Assert.That(facts.Source, Is.EqualTo(fiveLane ? DrumSourceFormat.FiveLane : DrumSourceFormat.FourLane));
            Assert.That(facts.OrdinaryKick && facts.ExtraKick && facts.OtherPlayable, Is.True);
            AssertMatchesLoaded(entry);
        });
    }

    [Test]
    public void EliteTerminatorOnlyTier_IsUnavailableToResolver()
    {
        WithDirectory(root =>
        {
            var elite = new TrackChunk(new SequenceTrackNameEvent("PART ELITE_DRUMS"));
            elite.Events.Add(new NoteOnEvent((SevenBitNumber) 72, (SevenBitNumber) 1) { DeltaTime = 9600 });
            elite.Events.Add(new NoteOffEvent((SevenBitNumber) 72, (SevenBitNumber) 0) { DeltaTime = 96 });
            string path = Path.Combine(root, "notes.mid");
            new MidiFile(new TrackChunk(), elite) { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) }.Write(path);
            var entry = Scan(root, path, ChartFormat.Mid);
            var facts = entry.AuthoredDrumSourceFacts.Single();

            Assert.That(facts.Source, Is.EqualTo(DrumSourceFormat.Elite));
            Assert.That(facts.HasPlayable, Is.False);
            Assert.That(DrumOutputResolver.Resolve(entry.AuthoredDrumSourceFacts, Instrument.EliteDrums,
                Difficulty.Expert, false, false, false), Is.Null);
            var loaded = entry.LoadChart()!.AuthoredDrumSources.Tiers[(DrumSourceFormat.Elite, Difficulty.Expert)];
            Assert.That(loaded.CloneEliteDifficulty().Notes.Single().IsInvisibleTerminator, Is.True,
                "The terminator remains in the loaded source as a control note.");
        });
    }

    [Test]
    public void EliteTerminator_DoesNotContaminatePlayableKickFacts()
    {
        WithDirectory(root =>
        {
            var elite = new TrackChunk(new SequenceTrackNameEvent("PART ELITE_DRUMS"));
            elite.Events.Add(new NoteOnEvent((SevenBitNumber) 72, (SevenBitNumber) 1) { DeltaTime = 9600 });
            elite.Events.Add(new NoteOffEvent((SevenBitNumber) 72, (SevenBitNumber) 0) { DeltaTime = 96 });
            elite.Events.Add(new NoteOnEvent((SevenBitNumber) 74, (SevenBitNumber) 100) { DeltaTime = 96 });
            elite.Events.Add(new NoteOffEvent((SevenBitNumber) 74, (SevenBitNumber) 0) { DeltaTime = 96 });
            string path = Path.Combine(root, "notes.mid");
            new MidiFile(new TrackChunk(), elite) { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) }.Write(path);
            var entry = Scan(root, path, ChartFormat.Mid);
            var facts = entry.AuthoredDrumSourceFacts.Single();

            Assert.That(facts.OrdinaryKick, Is.True);
            Assert.That(facts.ExtraKick, Is.False);
            Assert.That(facts.OtherPlayable, Is.False);
            Assert.That(facts.KickSuffix(false), Is.EqualTo(string.Empty));
            Assert.That(DrumOutputResolver.Resolve(entry.AuthoredDrumSourceFacts, Instrument.EliteDrums,
                Difficulty.Expert, false, false, false), Is.Not.Null);
            var loaded = entry.LoadChart()!.AuthoredDrumSources.Tiers[(DrumSourceFormat.Elite, Difficulty.Expert)];
            Assert.That(loaded.CloneEliteDifficulty().Notes.Count, Is.EqualTo(2));
            Assert.That(loaded.CloneEliteDifficulty().Notes.Count(note => note.IsInvisibleTerminator), Is.EqualTo(1));
        });
    }

    [Test]
    public void EliteFacts_PreservePairedKickFlamFromActualMidiParser()
    {
        WithDirectory(root =>
        {
            var track = new TrackChunk(new SequenceTrackNameEvent("PART ELITE_DRUMS"));
            track.Events.Add(new NoteOnEvent((SevenBitNumber) 74, (SevenBitNumber) 100) { DeltaTime = 192 });
            track.Events.Add(new NoteOnEvent((SevenBitNumber) 73, (SevenBitNumber) 100));
            track.Events.Add(new NoteOffEvent((SevenBitNumber) 74, (SevenBitNumber) 0) { DeltaTime = 96 });
            track.Events.Add(new NoteOffEvent((SevenBitNumber) 73, (SevenBitNumber) 0));
            var midi = new MidiFile(new TrackChunk(), track) { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) };
            string path = Path.Combine(root, "notes.mid");
            midi.Write(path);
            var entry = Scan(root, path, ChartFormat.Mid);
            var facts = entry.AuthoredDrumSourceFacts.Single();
            Assert.That(facts.PairedKickFlam && facts.OrdinaryKick && facts.ExtraKick, Is.True);
            AssertMatchesLoaded(entry);
        });
    }

    [Test]
    public void EliteGeneratedAvailability_DoesNotSuppressNativeLaterTier()
    {
        var midi = new MidiFile(new TrackChunk()) { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) };
        midi.Chunks.Add(Track("PART ELITE_DRUMS", 75));
        midi.Chunks.Add(Track("PART DRUMS", 84)); // Hard native, Expert Elite
        using var stream = new MemoryStream();
        midi.Write(stream);
        using var bytes = FixedArray<byte>.Alloc((int) stream.Length);
        stream.ToArray().CopyTo(bytes.Span);
        var parts = TestSongEntry.ParseMidiForTest(bytes);
        Assert.That(parts.FourLaneDrums[Difficulty.Hard], Is.True);
        Assert.That(parts.FourLaneDrums[Difficulty.Expert], Is.False);
    }

    [Test]
    public void SngMidiFacts_RoundTripThroughQuickAndFullCache()
    {
        WithDirectory(root =>
        {
            var midi = new MidiFile(new TrackChunk(), Track("PART DRUMS", 96, 95))
                { TimeDivision = new TicksPerQuarterNoteTimeDivision(192) };
            using var midiStream = new MemoryStream();
            midi.Write(midiStream);
            byte[] payload = midiStream.ToArray();
            string path = Path.Combine(root, "fixture.sng");
            using (var output = new BinaryWriter(File.Create(path)))
            {
                output.Write(System.Text.Encoding.ASCII.GetBytes("SNGPKG"));
                output.Write((uint) 1);
                output.Write(new byte[16]);
                using var metadata = new MemoryStream();
                using (var writer = new BinaryWriter(metadata, System.Text.Encoding.UTF8, true))
                {
                    writer.Write((ulong) 2);
                    foreach (var pair in new[] { ("name", "SNG fixture"), ("song_length", "60000") })
                    {
                        byte[] key = System.Text.Encoding.UTF8.GetBytes(pair.Item1);
                        byte[] value = System.Text.Encoding.UTF8.GetBytes(pair.Item2);
                        writer.Write(key.Length); writer.Write(key);
                        writer.Write(value.Length); writer.Write(value);
                    }
                }
                output.Write(metadata.Length); output.Write(metadata.ToArray());
                byte[] name = System.Text.Encoding.UTF8.GetBytes("notes.mid");
                byte[] audio = System.Text.Encoding.UTF8.GetBytes("song.opus");
                long listingLength = 8 + 1 + name.Length + 16 + 1 + audio.Length + 16;
                long position = output.BaseStream.Position + 8 + listingLength;
                output.Write(listingLength); output.Write((ulong) 2);
                output.Write((byte) name.Length); output.Write(name); output.Write((long) payload.Length); output.Write(position);
                output.Write((byte) audio.Length); output.Write(audio); output.Write((long) 0); output.Write(position + payload.Length);
                for (int i = 0; i < payload.Length; i++) output.Write((byte) (payload[i] ^ (byte) i));
            }
            string cache = Path.Combine(root, "cache.bin");
            SongEntry Run(bool quick) => YARG.Core.Song.Cache.CacheHandler.RunScan(quick, cache,
                Path.Combine(root, "bad.txt"), false, new System.Collections.Generic.List<string> { root })
                .Entries.Values.SelectMany(e => e).Single();
            var facts = Run(false).AuthoredDrumSourceFacts;
            Assert.That(facts.Single().OrdinaryKick && facts.Single().ExtraKick, Is.True);
            Assert.That(Run(true).AuthoredDrumSourceFacts, Is.EqualTo(facts));
            Assert.That(Run(false).AuthoredDrumSourceFacts, Is.EqualTo(facts));
            using (var old = File.OpenWrite(cache)) old.Write(26_09_25_00, Endianness.Little);
            Assert.That(Run(true).AuthoredDrumSourceFacts, Is.EqualTo(facts), "Old caches must trigger a rescan.");
        });
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
        File.WriteAllText(ini, "[song]\nname = Authored fixture\nsong_length = 60000\nfive_lane_drums = " + fiveLane + "\n");
        var result = UnpackedIniEntry.ProcessNewEntry(root, new FileInfo(path), format, new FileInfo(ini), "Tests");
        Assert.That(result.HasValue, Is.True, result.Error.ToString());
        return result.Value;
    }

    private static void AssertMatchesLoaded(SongEntry entry)
    {
        var loaded = entry.LoadChart()!;
        Assert.That(entry.AuthoredDrumSourceFacts, Is.EquivalentTo(loaded.AuthoredDrumSources.Tiers.Values.Select(t => t.Facts)));
    }

    private static void WithDirectory(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "yarg-authored-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, true); }
    }
}
