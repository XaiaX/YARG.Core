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
        Assert.That(observed.Where(o => !o.StartsWith("source-validation:", StringComparison.Ordinal) &&
            !o.StartsWith("scan-summary:", StringComparison.Ordinal) &&
            !o.StartsWith("scan-fallback:", StringComparison.Ordinal)),
            Is.EqualTo(new[] { "base:" + _package }));
        CacheHandler.ScanObservation = null;
        AssertComposition(Scan(update: update, upgrade: upgrade, quick: true));
    }

    [Test]
    public void YargupSourcesMatchLooseEntriesAndHashesThroughQuickAndScopedScans()
    {
        string updates = Path.Combine(_root, "updates");
        string upgrades = Path.Combine(_root, "upgrades");
        Directory.CreateDirectory(Path.Combine(updates, NODE));
        Directory.CreateDirectory(upgrades);
        string update = Path.Combine(updates, "songs_updates.dta");
        string upgrade = Path.Combine(upgrades, "upgrades.dta");
        File.WriteAllText(update, Dta("Selected update"));
        File.WriteAllBytes(Path.Combine(updates, NODE, NODE + "_update.mid"), Midi("PART GUITAR", 96));
        File.WriteAllText(upgrade, Dta("Selected upgrade"));
        File.WriteAllBytes(Path.Combine(upgrades, NODE + "_plus.mid"), Midi("PART ELITE_DRUMS", 75));
        var loose = Scan(update: update, upgrade: upgrade);
        string packageUpdate = Path.Combine(_root, "updates.yargup");
        string packageUpgrade = Path.Combine(_root, "upgrades.yargup");
        YARG.Core.Song.Supplements.SupplementPackageBuilder.Pack(update, YARG.Core.Song.Supplements.SupplementSourceKind.Updates, packageUpdate);
        YARG.Core.Song.Supplements.SupplementPackageBuilder.Pack(upgrade, YARG.Core.Song.Supplements.SupplementSourceKind.Upgrades, packageUpgrade);
        foreach (var sourcePair in new[] { (packageUpdate, packageUpgrade), (packageUpdate, upgrade), (update, packageUpgrade) })
        {
            File.Delete(CachePath); // Prove cold SHA-1 parity, not only reuse of the loose composition cache.
            foreach (bool quick in new[] { false, true })
            {
                var packaged = Scan(update: sourcePair.Item1, upgrade: sourcePair.Item2, quick: quick);
                AssertComposition(packaged);
                Assert.That(packaged.Hash, Is.EqualTo(loose.Hash));
            }
            var scoped = Scan(SongScanScope.Updates, sourcePair.Item1, sourcePair.Item2);
            AssertComposition(scoped);
            Assert.That(scoped.Hash, Is.EqualTo(loose.Hash));
        }
    }

    [Test]
    public void CorruptSelectedPackageDoesNotReplaceAppliedCache()
    {
        string directory = Path.Combine(_root, "updates");
        Directory.CreateDirectory(Path.Combine(directory, NODE));
        string dta = Path.Combine(directory, "songs_updates.dta");
        File.WriteAllText(dta, Dta("Selected update"));
        File.WriteAllBytes(Path.Combine(directory, NODE, NODE + "_update.mid"), Midi("PART GUITAR", 96));
        string package = Path.Combine(_root, "updates.yargup");
        WriteYargup(dta, "updates", package);
        var previous = Scan(update: package);
        byte[] cache = File.ReadAllBytes(CachePath);
        using (var archive = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Update))
        {
            var asset = archive.Entries.Single(e => e.FullName.StartsWith("blobs/"));
            string name = asset.FullName;
            byte[] bytes;
            using (var input = asset.Open()) { using var output = new MemoryStream(); input.CopyTo(output); bytes = output.ToArray(); }
            bytes[^1] ^= 1;
            asset.Delete();
            using var stream = archive.CreateEntry(name).Open(); stream.Write(bytes);
        }
        using var result = CacheHandler.RunScan(SongScanScope.Updates,
            new SongSourceConfiguration(new[] { _library }, package, null), false, CachePath, BadSongsPath, false);
        Assert.That(result.Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(File.ReadAllBytes(CachePath), Is.EqualTo(cache));
    }

    [Test]
    public void UnmatchedPackageMidiIsNotOpened()
    {
        string directory = Path.Combine(_root, "updates");
        Directory.CreateDirectory(Path.Combine(directory, "absent"));
        string dta = Path.Combine(directory, "songs_updates.dta");
        File.WriteAllText(dta, "(absent (name \"Not installed\"))");
        File.WriteAllBytes(Path.Combine(directory, "absent", "absent_update.mid"), new byte[] { 1, 2, 3 });
        string package = Path.Combine(_root, "updates.yargup");
        WriteYargup(dta, "updates", package, "absent");
        var entry = Scan(update: package);
        Assert.That(entry.Name.ToString(), Is.EqualTo("Packed base"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WrongPackageRoleCannotReplaceAppliedCache(bool selectedInBothSlots)
    {
        Scan();
        byte[] cache = File.ReadAllBytes(CachePath);
        string directory = Path.Combine(_root, "updates"); Directory.CreateDirectory(directory);
        string dta = Path.Combine(directory, "songs_updates.dta"); File.WriteAllText(dta, Dta("Metadata update"));
        string package = Path.Combine(_root, "updates.yargup"); WriteYargup(dta, "updates", package);
        using var result = CacheHandler.RunScan(SongScanScope.All,
            new SongSourceConfiguration(new[] { _library }, selectedInBothSlots ? package : null, package), false, CachePath, BadSongsPath, false);
        Assert.That(result.Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(result.Message, Does.Contain("source kind"));
        Assert.That(File.ReadAllBytes(CachePath), Is.EqualTo(cache));
    }

    [Test]
    public void PackageReplacementBeforePublicationLeavesAppliedCacheIntact()
    {
        string directory = Path.Combine(_root, "updates"); Directory.CreateDirectory(directory);
        string dta = Path.Combine(directory, "songs_updates.dta"); File.WriteAllText(dta, Dta("First title"));
        string package = Path.Combine(_root, "updates.yargup"); WriteYargup(dta, "updates", package);
        var old = Scan(update: package);
        byte[] cache = File.ReadAllBytes(CachePath);
        using var pending = CacheHandler.RunScan(SongScanScope.Updates,
            new SongSourceConfiguration(new[] { _library }, package, null), false, CachePath, BadSongsPath, false, deferCommit: true);
        Assert.That(pending.Status, Is.EqualTo(SongScanStatus.Success));
        File.WriteAllText(dta, Dta("Second title")); File.Delete(package); WriteYargup(dta, "updates", package);
        Assert.Throws<IOException>(() => pending.Commit());
        Assert.That(File.ReadAllBytes(CachePath), Is.EqualTo(cache));
        Assert.That(old.LoadChart(), Is.Null);
        var refreshed = Scan(SongScanScope.Updates, package);
        Assert.That(refreshed.Name.ToString(), Is.EqualTo("Second title"));
        Assert.That(refreshed.Hash, Is.EqualTo(old.Hash));
    }

    [Test]
    public void CancelledPackageSelectionLeavesAppliedCacheIntact()
    {
        Scan(); byte[] cache = File.ReadAllBytes(CachePath);
        string directory = Path.Combine(_root, "updates"); Directory.CreateDirectory(directory);
        string dta = Path.Combine(directory, "songs_updates.dta"); File.WriteAllText(dta, Dta("Pending title"));
        string package = Path.Combine(_root, "updates.yargup"); WriteYargup(dta, "updates", package);
        using (var pending = CacheHandler.RunScan(SongScanScope.Updates,
            new SongSourceConfiguration(new[] { _library }, package, null), false, CachePath, BadSongsPath, false, deferCommit: true))
            Assert.That(pending.Status, Is.EqualTo(SongScanStatus.Success));
        Assert.That(File.ReadAllBytes(CachePath), Is.EqualTo(cache));
    }

    [Test]
    public void WarmPackagedCompositionUsesSummariesWithoutOpeningMidiPayload()
    {
        string directory = Path.Combine(_root, "updates"); Directory.CreateDirectory(Path.Combine(directory, NODE));
        string dta = Path.Combine(directory, "songs_updates.dta"); File.WriteAllText(dta, Dta("Warm package"));
        File.WriteAllBytes(Path.Combine(directory, NODE, NODE + "_update.mid"), Midi("PART GUITAR", 96));
        string package = Path.Combine(_root, "updates.yargup");
        YARG.Core.Song.Supplements.SupplementPackageBuilder.Pack(dta, YARG.Core.Song.Supplements.SupplementSourceKind.Updates, package);
        var first = Scan(update: package);
        var observed = new List<string>(); CacheHandler.ScanObservation = observed.Add;
        var second = Scan(SongScanScope.Updates, package);
        Assert.That(second.Hash, Is.EqualTo(first.Hash));
        Assert.That(observed, Does.Contain("scan-summary:" + NODE));
        Assert.That(observed.Any(o => o.StartsWith("package-midi:", StringComparison.Ordinal)), Is.False);
        Assert.That(observed.Any(o => o.StartsWith("legacy-hash:", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void PackageIndexReadsDoNotGrowWithInstalledSongCount()
    {
        string directory = Path.Combine(_root, "updates"); Directory.CreateDirectory(directory);
        string dta = Path.Combine(directory, "songs_updates.dta"); File.WriteAllText(dta, Dta("Package title"));
        string package = Path.Combine(_root, "updates.yargup"); WriteYargup(dta, "updates", package);
        int opens = 0;
        YARG.Core.Song.Supplements.SupplementPackage.OpenObservation = _ => System.Threading.Interlocked.Increment(ref opens);
        try
        {
            Scan(update: package);
            int first = opens; opens = 0;
            for (int i = 0; i < 10; i++) File.Copy(_package, Path.Combine(_library, "duplicate" + i + ".con"));
            using var result = CacheHandler.RunScan(SongScanScope.All,
                new SongSourceConfiguration(new[] { _library }, package, null), false, CachePath, BadSongsPath, false);
            Assert.That(result.Status, Is.EqualTo(SongScanStatus.Success), result.Message);
            Assert.That(opens, Is.EqualTo(first), "Archive index parsing must be per-source, not per-installed-song");
        }
        finally { YARG.Core.Song.Supplements.SupplementPackage.OpenObservation = null; }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnsupportedMidiOrSummaryVersionRetainsLooseParity(bool unknownSummaryVersion)
    {
        string directory = Path.Combine(_root, "updates"); Directory.CreateDirectory(Path.Combine(directory, NODE));
        string dta = Path.Combine(directory, "songs_updates.dta"); File.WriteAllText(dta, Dta("Fallback package"));
        byte[] midi = Midi("PART GUITAR", 96);
        if (!unknownSummaryVersion)
        {
            // A valid key-signature event is outside the conservative summary eligibility gate.
            byte[] expanded = new byte[midi.Length + 6];
            midi.AsSpan(0, 22).CopyTo(expanded);
            new byte[] { 0, 0xff, 0x59, 2, 0, 0 }.CopyTo(expanded, 22);
            midi.AsSpan(22).CopyTo(expanded.AsSpan(28));
            BinaryPrimitives.WriteUInt32BigEndian(expanded.AsSpan(18), BinaryPrimitives.ReadUInt32BigEndian(midi.AsSpan(18)) + 6);
            midi = expanded;
        }
        File.WriteAllBytes(Path.Combine(directory, NODE, NODE + "_update.mid"), midi);
        var loose = Scan(update: dta);
        string package = Path.Combine(_root, "updates.yargup");
        YARG.Core.Song.Supplements.SupplementPackageBuilder.Pack(dta, YARG.Core.Song.Supplements.SupplementSourceKind.Updates, package);
        if (unknownSummaryVersion)
        {
            using var archive = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Update);
            var entry = archive.GetEntry("manifest.json")!;
            Newtonsoft.Json.Linq.JObject manifest;
            using (var reader = new StreamReader(entry.Open())) manifest = Newtonsoft.Json.Linq.JObject.Parse(reader.ReadToEnd());
            manifest["Assets"]![0]!["Summary"] = Convert.ToBase64String(BitConverter.GetBytes(int.MaxValue));
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()); writer.Write(manifest.ToString());
        }
        var observed = new List<string>(); CacheHandler.ScanObservation = observed.Add;
        var actual = Scan(update: package);
        Assert.That(actual.Name.ToString(), Is.EqualTo(loose.Name.ToString()));
        Assert.That(actual.Hash, Is.EqualTo(loose.Hash));
        Assert.That(actual[Instrument.FiveFretGuitar], Is.EqualTo(loose[Instrument.FiveFretGuitar]));
        Assert.That(actual.LoadChart(), Is.Not.Null);
        Assert.That(observed.Any(o => o.StartsWith("package-midi:", StringComparison.Ordinal)), Is.True);
        if (!unknownSummaryVersion) Assert.That(observed, Does.Contain("scan-fallback:" + NODE));
    }

    [Test]
    public void PackagedAuxiliaryAssetsRemainAvailableAfterScanHandlesClose()
    {
        string directory = Path.Combine(_root, "updates");
        string song = Path.Combine(directory, NODE); Directory.CreateDirectory(Path.Combine(song, "gen"));
        string dta = Path.Combine(directory, "songs_updates.dta"); File.WriteAllText(dta, Dta("Assets"));
        byte[] voc = { 1, 3, 5, 7 }; byte[] milo = { 2, 4, 6, 8 };
        File.WriteAllBytes(Path.Combine(song, NODE + ".voc"), voc);
        File.WriteAllBytes(Path.Combine(song, "gen", NODE + ".milo_xbox"), milo);
        byte[] image = new byte[40]; image[1] = 4; image[2] = 8; image[7] = 4; image[9] = 4;
        File.WriteAllBytes(Path.Combine(song, "gen", NODE + "_keep.png_xbox"), image);
        byte[] mogg = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(mogg, RBCONEntry.UNENCRYPTED_MOGG);
        BinaryPrimitives.WriteInt32LittleEndian(mogg.AsSpan(4), 8); mogg[8] = 99;
        File.WriteAllBytes(Path.Combine(song, NODE + "_update.mogg"), mogg);
        string package = Path.Combine(_root, "updates.yargup");
        YARG.Core.Song.Supplements.SupplementPackageBuilder.Pack(dta, YARG.Core.Song.Supplements.SupplementSourceKind.Updates, package);
        foreach (bool quick in new[] { false, true })
        {
            var entry = Scan(update: package, quick: quick);
            using var loadedVoc = entry.LoadVocData(); using var loadedMilo = entry.LoadMiloData();
            Assert.That(loadedVoc!.ReadOnlySpan.ToArray(), Is.EqualTo(voc));
            Assert.That(loadedMilo!.ReadOnlySpan.ToArray(), Is.EqualTo(milo));
            using var loadedImage = entry.LoadAlbumData(); Assert.That(loadedImage!.Width, Is.EqualTo(4));
            var getMogg = entry.GetType().GetMethod("GetMoggStream", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            using var stream = (Stream)getMogg.Invoke(entry, null)!;
            Assert.That(stream.CanSeek, Is.True); stream.Seek(8, SeekOrigin.Begin); Assert.That(stream.ReadByte(), Is.EqualTo(99));
            stream.Seek(0, SeekOrigin.Begin); Assert.That(stream.ReadByte(), Is.EqualTo(RBCONEntry.UNENCRYPTED_MOGG));
        }
    }

    [Test]
    public void WarmSummaryReuseDoesNotClaimBlobVerificationAndPlaybackRejectsCorruption()
    {
        string directory = Path.Combine(_root, "updates"); Directory.CreateDirectory(Path.Combine(directory, NODE));
        string dta = Path.Combine(directory, "songs_updates.dta"); File.WriteAllText(dta, Dta("Warm package"));
        File.WriteAllBytes(Path.Combine(directory, NODE, NODE + "_update.mid"), Midi("PART GUITAR", 96));
        string package = Path.Combine(_root, "updates.yargup");
        YARG.Core.Song.Supplements.SupplementPackageBuilder.Pack(dta, YARG.Core.Song.Supplements.SupplementSourceKind.Updates, package);
        var first = Scan(update: package);
        using (var archive = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Update))
        {
            var asset = archive.Entries.Single(e => e.FullName.StartsWith("blobs/"));
            string name = asset.FullName; byte[] bytes;
            using (var input = asset.Open()) { using var output = new MemoryStream(); input.CopyTo(output); bytes = output.ToArray(); }
            bytes[^1] ^= 1; asset.Delete(); using var stream = archive.CreateEntry(name).Open(); stream.Write(bytes);
        }
        var warm = Scan(SongScanScope.Updates, package);
        Assert.That(warm.Hash, Is.EqualTo(first.Hash));
        Assert.That(() => warm.LoadChart(), Throws.InstanceOf<IOException>());
        using var opened = YARG.Core.Song.Supplements.SupplementPackage.Open(package);
        Assert.That(() => opened.Verify(), Throws.InstanceOf<InvalidDataException>());
    }

    private static void WriteYargup(string dta, string kind, string path, string node = NODE)
    {
        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        void Add(string name, byte[] bytes) { using var stream = archive.CreateEntry(name).Open(); stream.Write(bytes); }
        byte[] original = File.ReadAllBytes(dta);
        Add("sources/original.dta", original);
        var assets = new List<object>();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(dta)!, "*", SearchOption.AllDirectories).Where(f => f != dta))
        {
            byte[] bytes = File.ReadAllBytes(file);
            string hash = Hash(bytes);
            Add("blobs/sha256/" + hash, bytes);
            assets.Add(new { Path = Path.GetRelativePath(Path.GetDirectoryName(dta)!, file).Replace('\\', '/'), Sha256 = hash, Length = bytes.Length });
        }
        Add("manifest.json", Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new {
            Format = "YARG supplementary source", Version = 1, Kind = kind, DtaPath = Path.GetFileName(dta),
            DtaSha256 = Hash(original), DtaLength = original.Length, Definitions = new[] { node }, Assets = assets })));
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
