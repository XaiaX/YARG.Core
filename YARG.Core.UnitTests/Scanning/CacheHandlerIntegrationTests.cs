using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.Extensions;
using YARG.Core.Song;
using YARG.Core.Song.Cache;

namespace YARG.Core.UnitTests.Scanning;

[Category("Integration")]
[NonParallelizable] // CacheHandler.Progress and the audio logging toggle are process-global.
public sealed class CacheHandlerIntegrationTests
{
    private const string NODE = "testsong";
    private string _root = null!;
    private string _library = null!;
    private string _cachePath = null!;
    private string _badSongsPath = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "yarg-cache-integration-" + Guid.NewGuid().ToString("N"));
        _library = Path.Combine(_root, "library");
        _cachePath = Path.Combine(_root, "songcache.bin");
        _badSongsPath = Path.Combine(_root, "badsongs.txt");
        string songs = Path.Combine(_library, "songs");
        string song = Path.Combine(songs, NODE);
        Directory.CreateDirectory(song);
        File.WriteAllText(Path.Combine(songs, "songs.dta"), Dta("Base"));
        WriteMidi(Path.Combine(song, NODE + ".mid"), "PART BASS", true);
        WriteMogg(Path.Combine(song, NODE + ".mogg"), RBCONEntry.UNENCRYPTED_MOGG);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void ModeFlip_RebuildsCacheInsteadOfReusingOppositeMode()
    {
        string alpha = AddUpdate("alpha", "Alpha", "PART GUITAR", false);
        string zeta = AddUpdate("zeta", "Zeta", "PART GUITAR", true);
        File.SetLastWriteTimeUtc(Path.Combine(alpha, RBCONEntry.SONGUPDATES_DTA), DateTime.UtcNow.AddMinutes(2));
        File.SetLastWriteTimeUtc(Path.Combine(zeta, RBCONEntry.SONGUPDATES_DTA), DateTime.UtcNow.AddMinutes(-2));

        AssertEntry(Scan(false, false), "Alpha", false);
        AssertEntry(Scan(true, true), "Zeta", true);
        AssertEntry(Scan(false, true), "Zeta", true);
        AssertEntry(Scan(true, false), "Alpha", false);
        Assert.That(File.Exists(_cachePath), Is.True);
    }

    [Test]
    public void CumulativeUpdates_UsePackageNameOrderRegardlessOfDiscoveryOrTimestamps()
    {
        AddUpdate("zeta", "Zeta", "PART GUITAR", true);
        AddUpdate("alpha", "Alpha", "PART GUITAR", false);
        // Reverse the chronological order: layering must follow package names, not timestamps.
        File.SetLastWriteTimeUtc(Path.Combine(_library, "alpha", "songs_updates", RBCONEntry.SONGUPDATES_DTA),
            DateTime.UtcNow.AddMinutes(2));
        File.SetLastWriteTimeUtc(Path.Combine(_library, "zeta", "songs_updates", RBCONEntry.SONGUPDATES_DTA),
            DateTime.UtcNow.AddMinutes(-2));

        var entry = Scan(false, true);
        Assert.Multiple(() =>
        {
            Assert.That(entry.Name.ToString(), Is.EqualTo("Zeta"));
            Assert.That(entry[Instrument.FiveFretGuitar].IsActive(), Is.True);
            Assert.That(entry[Instrument.FiveFretBass].IsActive(), Is.True);
        });
        Assert.That(Scan(false, true).Name.ToString(), Is.EqualTo("Zeta"));
    }

    [Test]
    public void CumulativeCache_RoundTripsThroughQuickAndFullScans()
    {
        AddUpdate("alpha", "Alpha", "PART GUITAR", false);
        AddUpdate("zeta", "Zeta", "PART GUITAR", true);

        AssertEntry(Scan(false, true), "Zeta", true);
        Assert.That(File.Exists(_cachePath), Is.True);
        AssertEntry(Scan(true, true), "Zeta", true);
        AssertEntry(Scan(false, true), "Zeta", true);
        Assert.That(File.Exists(_badSongsPath), Is.False);
    }

    [Test]
    public void AssetOnlyMutation_QuickScanKeepsCachedEntryButFullScanInvalidatesIt()
    {
        string update = AddUpdate("alpha", "Alpha", "PART GUITAR", true);
        string mogg = Path.Combine(update, NODE, NODE + "_update.mogg");
        AssertEntry(Scan(false, true), "Alpha", true);

        // Add only an update asset: neither the DTA nor the MIDI is touched.
        // Truncated header (not merely an unsupported version) so the error is update-specific.
        File.WriteAllBytes(mogg, new byte[] { 0x0A });

        AssertEntry(Scan(true, true), "Alpha", true);
        Assert.That(ScanAll(false, true).Entries.Values.Sum(entries => entries.Count), Is.Zero,
            "A full scan must reject the now-invalid update MOGG despite unchanged DTA and MIDI timestamps.");
        Assert.That(File.ReadAllText(_badSongsPath), Does.Contain("update").IgnoreCase);
    }

    private SongCache ScanAll(bool quick, bool cumulative) => CacheHandler.RunScan(quick, _cachePath,
        _badSongsPath, false, new List<string> { _library }, cumulative);

    private RBCONEntry Scan(bool quick, bool cumulative)
    {
        var entries = ScanAll(quick, cumulative).Entries.Values.SelectMany(entries => entries).ToArray();
        Assert.That(entries, Has.Length.EqualTo(1),
            $"Expected exactly one valid unpacked RBCON. Bad songs: {(File.Exists(_badSongsPath) ? File.ReadAllText(_badSongsPath) : "none")}");
        Assert.That(entries[0], Is.InstanceOf<RBCONEntry>());
        return (RBCONEntry) entries[0];
    }

    private static void AssertEntry(RBCONEntry entry, string name, bool guitar)
    {
        Assert.Multiple(() =>
        {
            Assert.That(entry.Name.ToString(), Is.EqualTo(name));
            Assert.That(entry[Instrument.FiveFretGuitar].IsActive(), Is.EqualTo(guitar));
            Assert.That(entry[Instrument.FiveFretBass].IsActive(), Is.True);
        });
    }

    private string AddUpdate(string package, string title, string track, bool notes)
    {
        string update = Path.Combine(_library, package, "songs_updates");
        string song = Path.Combine(update, NODE);
        Directory.CreateDirectory(song);
        File.WriteAllText(Path.Combine(update, RBCONEntry.SONGUPDATES_DTA), Dta(title));
        WriteMidi(Path.Combine(song, NODE + "_update.mid"), track, notes);
        return update;
    }

    private static string Dta(string title) => $$"""
        ({{NODE}}
          (name "{{title}}")
          (song
            (name "songs/{{NODE}}/{{NODE}}")
            (pans (0.0))
            (vols (0.0))
            (cores (0.0))
          )
        )
        """;

    private static void WriteMogg(string path, int version)
    {
        using var output = File.Create(path);
        output.Write(version, Endianness.Little);
    }

    private static void WriteMidi(string path, string track, bool notes)
    {
        var midi = new MidiFile(new TrackChunk())
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };
        var chunk = new TrackChunk(new SequenceTrackNameEvent(track));
        if (notes)
        {
            chunk.Events.Add(new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) 96,
                (Melanchall.DryWetMidi.Common.SevenBitNumber) 100) { DeltaTime = 1 });
            chunk.Events.Add(new NoteOffEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) 96,
                (Melanchall.DryWetMidi.Common.SevenBitNumber) 0) { DeltaTime = 120 });
        }
        midi.Chunks.Add(chunk);
        midi.Write(path);
    }
}
