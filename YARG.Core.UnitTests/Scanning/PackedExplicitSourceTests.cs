using System.Buffers.Binary;
using System.Text;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.IO;
using YARG.Core.Song;
using YARG.Core.Song.Cache;

namespace YARG.Core.UnitTests.Scanning;

[Category("Integration")]
[NonParallelizable] // CacheHandler observation and progress are process-global.
public sealed class PackedExplicitSourceTests
{
    private const string NODE = "synthetic";
    private string _root = null!;
    private string _library = null!;
    private string _package = null!;
    private string CachePath => Path.Combine(_root, "songcache.bin");
    private string BadSongsPath => Path.Combine(_root, "badsongs.txt");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "yarg-packed-explicit-" + Guid.NewGuid().ToString("N"));
        _library = Path.Combine(_root, "library");
        Directory.CreateDirectory(_library);
        _package = Path.Combine(_library, "mixed.con");
        // Original synthetic DTA, single-note MIDI, and header-only MOGG. No game assets,
        // external writer, signatures, or third-party fixture data are needed by Core.
        WritePackage(_package, new Dictionary<string, byte[]>
        {
            ["songs/songs.dta"] = Encoding.UTF8.GetBytes(Dta("Packed base")),
            [$"songs/{NODE}/{NODE}.mid"] = Midi("PART BASS", 96),
            [$"songs/{NODE}/{NODE}.mogg"] = BitConverter.GetBytes(RBCONEntry.UNENCRYPTED_MOGG),
            ["songs_upgrades/upgrades.dta"] = Encoding.UTF8.GetBytes(Dta("Implicit upgrade")),
            [$"songs_upgrades/{NODE}_plus.mid"] = Midi("PART ELITE_DRUMS", 75)
        });
    }

    [TearDown]
    public void TearDown()
    {
        CacheHandler.ScanObservation = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public void ExplicitEmptySourcesRetainPackedBaseButSuppressEmbeddedUpgrade()
    {
        // Prove the embedded upgrade is usable, rather than passing because it is malformed.
        var discovered = CacheHandler.RunScan(false, CachePath, BadSongsPath, false,
            new List<string> { _library }, false);
        Assert.That(OnlyEntry(discovered).DrumSourcePresence.Elite, Is.EqualTo(DifficultyMask.Expert));

        foreach (bool quick in new[] { false, true })
        {
            var entry = Scan(quick: quick);
            Assert.That(entry.Name.ToString(), Is.EqualTo("Packed base"));
            Assert.That(entry[Instrument.FiveFretBass].IsActive(), Is.True);
            Assert.That(entry.DrumSourcePresence.Elite, Is.EqualTo(DifficultyMask.None));
            Assert.That(entry.LoadChart(), Is.Not.Null);
        }
    }

    [Test]
    public void ArbitraryExternalNamesComposeWithPackedBaseThroughCacheAndScopedRefresh()
    {
        string updates = Path.Combine(_root, "chosen-update");
        string upgrades = Path.Combine(_root, "chosen-upgrade");
        Directory.CreateDirectory(Path.Combine(updates, NODE));
        Directory.CreateDirectory(upgrades);
        string update = Path.Combine(updates, "my-arrangement.dta");
        string upgrade = Path.Combine(upgrades, "my-drums.dta");
        File.WriteAllText(update, Dta("Selected update"));
        File.WriteAllBytes(Path.Combine(updates, NODE, NODE + "_update.mid"), Midi("PART GUITAR", 96));
        File.WriteAllText(upgrade, Dta("Selected upgrade"));
        File.WriteAllBytes(Path.Combine(upgrades, NODE + "_plus.mid"), Midi("PART ELITE_DRUMS", 75));

        foreach (bool quick in new[] { false, true })
            AssertComposition(Scan(update: update, upgrade: upgrade, quick: quick));

        // Supplement-only work must rebuild just the cached packed base, not traverse the library.
        // This seam observes base reopens, not every filesystem open.
        WritePackage(Path.Combine(_library, "unrelated.con"), new Dictionary<string, byte[]>
        {
            ["readme.txt"] = Encoding.UTF8.GetBytes("Synthetic unrelated package")
        });
        var observed = new List<string>();
        CacheHandler.ScanObservation = observed.Add;
        AssertComposition(Scan(SongScanScope.Updates, update, upgrade));
        Assert.That(observed, Is.EqualTo(new[] { "base:" + _package }));
        CacheHandler.ScanObservation = null;
        AssertComposition(Scan(update: update, upgrade: upgrade, quick: true));
    }

    private RBCONEntry Scan(SongScanScope scope = SongScanScope.All, string? update = null,
        string? upgrade = null, bool quick = false)
    {
        using var result = CacheHandler.RunScan(scope,
            new SongSourceConfiguration(new[] { _library }, update, upgrade), quick,
            CachePath, BadSongsPath, false);
        Assert.That(result.Status, Is.EqualTo(SongScanStatus.Success), result.Message);
        return OnlyEntry(result.Cache!);
    }

    private static RBCONEntry OnlyEntry(SongCache cache) =>
        (RBCONEntry) cache.Entries.Values.SelectMany(entries => entries).Single();

    private static void AssertComposition(RBCONEntry entry)
    {
        Assert.That(entry.Name.ToString(), Is.EqualTo("Selected update"));
        Assert.That(entry[Instrument.FiveFretBass].IsActive(), Is.True);
        Assert.That(entry[Instrument.FiveFretGuitar].IsActive(), Is.True);
        Assert.That(entry.DrumSourcePresence.Elite, Is.EqualTo(DifficultyMask.Expert));
        Assert.That(entry.LoadChart(), Is.Not.Null);
    }

    private static string Dta(string title) => $$"""
        ({{NODE}}
          (name "{{title}}")
          (song (name "songs/{{NODE}}/{{NODE}}")
            (pans (0.0)) (vols (0.0)) (cores (0.0)))
        )
        """;

    private static byte[] Midi(string track, byte note)
    {
        var midi = new MidiFile(new TrackChunk(), new TrackChunk(new SequenceTrackNameEvent(track),
            new NoteOnEvent((SevenBitNumber) note, (SevenBitNumber) 100) { DeltaTime = 1 },
            new NoteOffEvent((SevenBitNumber) note, (SevenBitNumber) 0) { DeltaTime = 120 }))
        { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        using var output = new MemoryStream();
        midi.Write(output);
        return output.ToArray();
    }

    // Minimal original STFS image builder using the layout exercised by ConHandlerTests.
    // Deliberately bounded: one file-table block, one contiguous block per file, shift zero.
    // It models only the unsigned container data read by Core, not an Xbox-installable package.
    private static void WritePackage(string path, Dictionary<string, byte[]> files)
    {
        var entries = new List<(string Name, short Parent, byte[]? Data)>();
        var directories = new Dictionary<string, short>();
        foreach (var (name, data) in files)
        {
            string[] parts = name.Split('/');
            short parent = -1;
            string directory = "";
            foreach (string part in parts[..^1])
            {
                directory += "/" + part;
                if (!directories.TryGetValue(directory, out short index))
                {
                    index = checked((short) entries.Count);
                    directories.Add(directory, index);
                    entries.Add((part, parent, null));
                }
                parent = index;
            }
            entries.Add((parts[^1], parent, data));
        }
        Assert.That(entries.Count, Is.LessThan(64), "Fixture exceeds one file-table block.");
        var image = new byte[CONFileStream.CalculateBlockLocation(files.Count, 0) + CONFileStream.BYTES_PER_BLOCK];
        Encoding.ASCII.GetBytes("CON ").CopyTo(image, 0);
        BinaryPrimitives.WriteInt32BigEndian(image.AsSpan(0x340), 0xB000);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x37C), 1);
        // File-table first block is zero (the image is zero-initialized).
        int table = (int) CONFileStream.CalculateBlockLocation(0, 0);
        int block = 1;
        for (int i = 0; i < entries.Count; i++)
        {
            var (name, parent, data) = entries[i];
            Span<byte> listing = image.AsSpan(table + i * 0x40, 0x40);
            byte[] encoded = Encoding.UTF8.GetBytes(name);
            Assert.That(encoded.Length, Is.LessThanOrEqualTo(0x28));
            encoded.CopyTo(listing);
            listing[0x28] = (byte) (encoded.Length | (byte) (data == null
                ? CONFileListing.Flag.Directory : CONFileListing.Flag.Consecutive));
            BinaryPrimitives.WriteInt16BigEndian(listing[0x32..], parent);
            if (data == null) continue;
            Assert.That(data.Length, Is.LessThanOrEqualTo(CONFileStream.BYTES_PER_BLOCK));
            listing[0x29] = 1;
            listing[0x2F] = checked((byte) block);
            BinaryPrimitives.WriteInt32BigEndian(listing[0x34..], data.Length);
            data.CopyTo(image, (int) CONFileStream.CalculateBlockLocation(block++, 0));
        }
        File.WriteAllBytes(path, image);
    }
}
