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

    [Test]
    public void DrumPresence_EffectiveReplacementAndQuickFullCacheRoundTripWithoutFullLoads()
    {
        using var observation = YARG.Core.Chart.FullLoadObservation.Begin();
        AddUpdate("alpha", "Alpha", "PART DRUMS", true);
        var presence = Scan(false, true).DrumSourcePresence;
        Assert.That(presence.FourLane, Is.EqualTo(DifficultyMask.Expert));
        Assert.That(presence.FiveLane, Is.EqualTo(DifficultyMask.None));
        Assert.That(presence.Elite, Is.EqualTo(DifficultyMask.None));
        Assert.That(Scan(true, true).DrumSourcePresence, Is.EqualTo(presence));
        Assert.That(Scan(false, true).DrumSourcePresence, Is.EqualTo(presence));

        // A later named empty track replaces the earlier drums, while base bass remains valid.
        AddUpdate("zeta", "Zeta", "PART DRUMS", false);
        Assert.That(Scan(false, true).DrumSourcePresence, Is.EqualTo(default(YARG.Core.Game.DrumSourcePresence)));
        Assert.That(Scan(true, true).DrumSourcePresence, Is.EqualTo(default(YARG.Core.Game.DrumSourcePresence)));
        Assert.That(Scan(false, true).DrumSourcePresence, Is.EqualTo(default(YARG.Core.Game.DrumSourcePresence)));
        Assert.That(observation.MidiLoads, Is.Zero);
        Assert.That(observation.DotChartLoads, Is.Zero);
        Assert.That(observation.AuthoredNoteBatches, Is.Zero);
        Assert.That(observation.AuthoredNotes, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UpdateAddsEliteAndKeys_LaterEmptyDrumsRetainsOtherSources(bool eliteFirst)
    {
        string update = AddUpdate("alpha", "Alpha", "PART DRUMS", true);
        TrackChunk Part(string name, int pitch) => new(new SequenceTrackNameEvent(name),
            new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) pitch,
                (Melanchall.DryWetMidi.Common.SevenBitNumber) 100) { DeltaTime = 1 },
            new NoteOffEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) pitch,
                (Melanchall.DryWetMidi.Common.SevenBitNumber) 0) { DeltaTime = 120 });
        var classic = Part("PART DRUMS", 96);
        var elite = Part("PART ELITE_DRUMS", 75);
        var midi = new MidiFile(new TrackChunk(), eliteFirst ? elite : classic,
            eliteFirst ? classic : elite, Part("PART KEYS", 96))
            { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        midi.Write(Path.Combine(update, NODE, NODE + "_update.mid"), true);
        using var observation = YARG.Core.Chart.FullLoadObservation.Begin();
        foreach (bool quick in new[] { false, true, false })
        {
            var entry = Scan(quick, true);
            Assert.That(entry.DrumSourcePresence.FourLane, Is.EqualTo(DifficultyMask.Expert));
            Assert.That(entry.DrumSourcePresence.Elite, Is.EqualTo(DifficultyMask.Expert));
            Assert.That(entry[Instrument.Keys][Difficulty.Expert], Is.True);
        }
        AddUpdate("zeta", "Zeta", "PART DRUMS", false);
        foreach (bool quick in new[] { false, true, false })
        {
            var entry = Scan(quick, true);
            Assert.That(entry.DrumSourcePresence.FourLane, Is.EqualTo(DifficultyMask.None));
            Assert.That(entry.DrumSourcePresence.Elite, Is.EqualTo(DifficultyMask.Expert));
            Assert.That(entry[Instrument.Keys][Difficulty.Expert], Is.True);
            Assert.That(entry[Instrument.FiveFretBass].IsActive(), Is.True);
        }
        Assert.That(observation.MidiLoads, Is.Zero);
        Assert.That(observation.DotChartLoads, Is.Zero);
        Assert.That(observation.AuthoredNoteBatches, Is.Zero);
        Assert.That(observation.AuthoredNotes, Is.Zero);
    }

    private SongScanResult Explicit(SongScanScope scope, string? update = null, string? upgrade = null, bool quick = false, bool deferred = false) =>
        CacheHandler.RunScan(scope, new SongSourceConfiguration(new[] { _library }, update, upgrade), quick,
            _cachePath, _badSongsPath, false, deferred);

    private static RBCONEntry SuccessfulEntry(SongScanResult result)
    {
        Assert.That(result.Status, Is.EqualTo(SongScanStatus.Success), result.Message);
        return (RBCONEntry) result.Cache!.Entries.Values.SelectMany(v => v).Single();
    }

    [Test]
    public void ExplicitSourcesIgnoreDiscoveredLooseSupplements()
    {
        AddUpdate("implicit", "Hidden", "PART GUITAR", true);
        var entry = SuccessfulEntry(Explicit(SongScanScope.All));
        Assert.That(entry.Name.ToString(), Is.EqualTo("Base"));
        Assert.That(entry[Instrument.FiveFretGuitar].IsActive(), Is.False);
    }

    [Test]
    public void ArbitraryDtaNamesSurviveCacheAndPlayback()
    {
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        string path = Path.Combine(root, "anything.dta");
        File.Move(Path.Combine(root, RBCONEntry.SONGUPDATES_DTA), path);
        foreach (bool quick in new[] { false, true })
        {
            var entry = SuccessfulEntry(Explicit(SongScanScope.All, path, quick: quick));
            AssertEntry(entry, "Selected", true);
            Assert.That(entry.LoadChart(), Is.Not.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MetadataOnlyUpdatesPreserveBaseChartAcrossScanScopes(bool discUpdate)
    {
        string directory = Path.Combine(_root, "metadata");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "selected.dta");
        string marker = discUpdate ? " (extra_authoring (disc_update))" : string.Empty;
        File.WriteAllText(path, "(" + NODE + " (name \"Metadata\")" + marker + ")\n" +
            "(missing (name \"Unmatched\")" + marker + ")\n" +
            "(missing (artist \"Repeated unmatched definition\")" + marker + ")");

        foreach (var scan in new[]
        {
            (SongScanScope.All, false), (SongScanScope.All, true),
            (SongScanScope.Updates, false), (SongScanScope.Upgrades, false),
            (SongScanScope.Songs, false), (SongScanScope.All, true)
        })
        {
            using var result = Explicit(scan.Item1, path, quick: scan.Item2);
            var entry = SuccessfulEntry(result);
            AssertEntry(entry, "Metadata", false);
            Assert.That(result.UpdatesDefinitionCount, Is.EqualTo(3));
            Assert.That(result.UpgradesDefinitionCount, Is.Zero);
            var chart = entry.LoadChart();
            Assert.That(chart, Is.Not.Null);
            Assert.That(chart!.FiveFretBass.GetDifficulty(Difficulty.Expert).Notes, Has.Count.EqualTo(1));
            Assert.That(chart.FiveFretGuitar.GetDifficulty(Difficulty.Expert).Notes, Is.Empty);
        }
        Assert.That(File.Exists(_badSongsPath), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeletedAppliedUpdateMidiRequiresSelectedRescan(bool quickReload)
    {
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        File.WriteAllText(path, "(" + NODE + " (name \"Selected\") (extra_authoring (disc_update)))");
        var entry = SuccessfulEntry(Explicit(SongScanScope.All, path));
        if (quickReload) entry = SuccessfulEntry(Explicit(SongScanScope.All, path, quick: true));
        AssertEntry(entry, "Selected", true);
        Assert.That(entry.LoadChart(), Is.Not.Null);
        byte[] before = File.ReadAllBytes(_cachePath);
        File.Delete(Path.Combine(root, NODE, NODE + "_update.mid"));

        Assert.That(entry.LoadChart(), Is.Null);
        using (var rejected = Explicit(SongScanScope.Upgrades, path))
        {
            Assert.That(rejected.Status, Is.EqualTo(SongScanStatus.SourceError));
            Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(before));
        }
        using (var refreshed = Explicit(SongScanScope.Updates, path))
        {
            var metadataOnly = SuccessfulEntry(refreshed);
            AssertEntry(metadataOnly, "Selected", false);
            Assert.That(refreshed.UpdatesDefinitionCount, Is.EqualTo(1));
            Assert.That(metadataOnly.LoadChart(), Is.Not.Null);
        }
        using var quick = Explicit(SongScanScope.All, path, quick: true);
        var reloaded = SuccessfulEntry(quick);
        AssertEntry(reloaded, "Selected", false);
        Assert.That(quick.UpdatesDefinitionCount, Is.EqualTo(1));
        Assert.That(reloaded.LoadChart(), Is.Not.Null);
    }

    [Test]
    public void ExplicitChangedDtaAppliesOnFirstRefresh()
    {
        string root = AddUpdate("selected", "First", "PART GUITAR", true);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        SuccessfulEntry(Explicit(SongScanScope.All, path));
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, Dta("Later"));
        File.SetLastWriteTimeUtc(path, time);
        Assert.That(SuccessfulEntry(Explicit(SongScanScope.Updates, path)).Name.ToString(), Is.EqualTo("Later"));
        Assert.That(SuccessfulEntry(Explicit(SongScanScope.All, path, quick: true)).Name.ToString(), Is.EqualTo("Later"));
    }

    [Test]
    public void ScopedRefreshChangeRemovalMatrix()
    {
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        SuccessfulEntry(Explicit(SongScanScope.All));
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.Updates, path)), "Selected", true);
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.Updates)), "Base", false);
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.All, quick: true)), "Base", false);
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.Updates, path)), "Selected", true);
        File.WriteAllText(path, string.Empty);
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.Updates, path)), "Base", false);
    }

    [Test]
    public void ScopedRefreshDoesNotTraverseBaseLibrary()
    {
        SuccessfulEntry(Explicit(SongScanScope.All));
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        var observed = new List<string>();
        CacheHandler.ScanObservation = observed.Add;
        try
        {
            AssertEntry(SuccessfulEntry(Explicit(SongScanScope.Updates, Path.Combine(root, RBCONEntry.SONGUPDATES_DTA))), "Selected", true);
            Assert.That(observed.Any(p => p.StartsWith("directory:")), Is.False);
            Assert.That(observed.Count(p => p.StartsWith("base:")), Is.EqualTo(1));
        }
        finally { CacheHandler.ScanObservation = null; }
    }

    [Test]
    public void PartialRefreshRequiresValidInventory()
    {
        Assert.That(Explicit(SongScanScope.Updates).Status, Is.EqualTo(SongScanStatus.NeedsFullScan));
        SuccessfulEntry(Explicit(SongScanScope.All));
        var result = CacheHandler.RunScan(SongScanScope.Upgrades, new SongSourceConfiguration(new[] { _library, _root }),
            false, _cachePath, _badSongsPath, false);
        Assert.That(result.Status, Is.EqualTo(SongScanStatus.NeedsFullScan));
        File.Delete(Path.Combine(_library, "songs", "songs.dta"));
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        Assert.That(Explicit(SongScanScope.Updates, Path.Combine(root, RBCONEntry.SONGUPDATES_DTA)).Status,
            Is.EqualTo(SongScanStatus.NeedsFullScan));
    }

    [Test]
    public void ScopedRefreshUsesAppliedOtherSource()
    {
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        SuccessfulEntry(Explicit(SongScanScope.All, path));
        var result = Explicit(SongScanScope.Upgrades); // Pending clear of Updates must not apply.
        AssertEntry(SuccessfulEntry(result), "Selected", true);
        Assert.That(result.AppliedSources!.UpdatesDtaPath, Is.EqualTo(path));
        var time = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, Dta("Modified"));
        File.SetLastWriteTimeUtc(path, time);
        result = Explicit(SongScanScope.Upgrades);
        Assert.That(result.Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(result.Message, Does.Contain("Updates"));
    }

    [Test]
    public void FailedRefreshPreservesPublishedSnapshotAndCache()
    {
        var prior = SuccessfulEntry(Explicit(SongScanScope.All));
        byte[] bytes = File.ReadAllBytes(_cachePath);
        Assert.That(Explicit(SongScanScope.Updates, Path.Combine(_root, "missing.dta")).Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(bytes));
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        CacheHandler.BeforeCacheCommit = () => throw new IOException("Injected replace failure");
        try
        {
            var result = Explicit(SongScanScope.Updates, Path.Combine(root, RBCONEntry.SONGUPDATES_DTA));
            Assert.That(result.Status, Is.EqualTo(SongScanStatus.SourceError));
            Assert.That(result.Cache, Is.Null);
            Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(bytes));
            Assert.That(prior.Name.ToString(), Is.EqualTo("Base"));
            Assert.That(prior.LoadChart(), Is.Not.Null);
        }
        finally { CacheHandler.BeforeCacheCommit = null; }
    }

    [Test]
    public void ExplicitUpdateAndEliteUpgradeCompose()
    {
        string root = AddUpdate("selected", "Update", "PART GUITAR", true);
        string update = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        string upgradeRoot = Path.Combine(_root, "elite");
        Directory.CreateDirectory(upgradeRoot);
        string upgrade = Path.Combine(upgradeRoot, "my-elite.dta");
        File.WriteAllText(upgrade, Dta("Upgrade"));
        var elite = new MidiFile(new TrackChunk(), new TrackChunk(new SequenceTrackNameEvent("PART ELITE_DRUMS"),
            new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) 75, (Melanchall.DryWetMidi.Common.SevenBitNumber) 100) { DeltaTime = 1 },
            new NoteOffEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) 75, (Melanchall.DryWetMidi.Common.SevenBitNumber) 0) { DeltaTime = 120 }))
            { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
        elite.Write(Path.Combine(upgradeRoot, NODE + "_plus.mid"));
        foreach (bool quick in new[] { false, true })
        {
            var entry = SuccessfulEntry(Explicit(SongScanScope.All, update, upgrade, quick));
            AssertEntry(entry, "Update", true);
            Assert.That(entry.DrumSourcePresence.Elite, Is.EqualTo(DifficultyMask.Expert));
            Assert.That(entry.LoadChart(), Is.Not.Null);
        }
        var unrelatedRefresh = SuccessfulEntry(Explicit(SongScanScope.Updates, update, upgrade));
        Assert.That(unrelatedRefresh.LoadChart(), Is.Not.Null);
        Assert.That(unrelatedRefresh.DrumSourcePresence.Elite, Is.EqualTo(DifficultyMask.Expert));
        var removed = SuccessfulEntry(Explicit(SongScanScope.Upgrades, update));
        AssertEntry(removed, "Update", true);
        Assert.That(removed.DrumSourcePresence.Elite, Is.EqualTo(DifficultyMask.None));
    }

    [TestCase("midi")]
    [TestCase("asset")]
    [TestCase("deleted")]
    public void AppliedDependenciesFailClosedAfterRestart(string mutation)
    {
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        var entry = SuccessfulEntry(Explicit(SongScanScope.All, path));
        entry = SuccessfulEntry(Explicit(SongScanScope.All, path, quick: true));
        string midi = Path.Combine(root, NODE, NODE + "_update.mid");
        if (mutation == "midi") File.SetLastWriteTimeUtc(midi, DateTime.UtcNow.AddMinutes(2));
        else if (mutation == "asset") File.WriteAllBytes(Path.Combine(root, NODE, NODE + "_update.mogg"), new byte[] { 0 });
        else File.Delete(midi);
        byte[] bytes = File.ReadAllBytes(_cachePath);
        var result = Explicit(SongScanScope.Upgrades, path);
        Assert.That(result.Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(result.Message, Does.Contain("Updates"));
        Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(bytes));
        Assert.That(entry.LoadChart(), Is.Null);
    }

    [Test]
    public void AffectedBaseMidiChangeRequiresFullScan()
    {
        SuccessfulEntry(Explicit(SongScanScope.All));
        File.SetLastWriteTimeUtc(Path.Combine(_library, "songs", NODE, NODE + ".mid"), DateTime.UtcNow.AddMinutes(2));
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        Assert.That(Explicit(SongScanScope.Updates, Path.Combine(root, RBCONEntry.SONGUPDATES_DTA)).Status,
            Is.EqualTo(SongScanStatus.NeedsFullScan));
    }

    [Test]
    public void InvalidSelectedDtaPreservesCache()
    {
        SuccessfulEntry(Explicit(SongScanScope.All));
        byte[] bytes = File.ReadAllBytes(_cachePath);
        string path = Path.Combine(_root, "broken.dta");
        File.WriteAllText(path, "(truncated");
        Assert.That(Explicit(SongScanScope.Updates, path).Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(bytes));
        File.WriteAllText(path, "; empty definitions are valid\n");
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.Updates, path)), "Base", false);
    }

    [Test]
    public void AllMatchingBaseCopiesAreRebuilt()
    {
        string copySongs = Path.Combine(_library, "copy", "songs");
        string copy = Path.Combine(copySongs, NODE);
        Directory.CreateDirectory(copy);
        File.WriteAllText(Path.Combine(copySongs, "songs.dta"), Dta("Copy"));
        WriteMidi(Path.Combine(copy, NODE + ".mid"), "PART BASS", true);
        WriteMogg(Path.Combine(copy, NODE + ".mogg"), RBCONEntry.UNENCRYPTED_MOGG);
        Assert.That(Explicit(SongScanScope.All).Cache!.Entries.Values.Sum(v => v.Count), Is.EqualTo(2));
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        var result = Explicit(SongScanScope.Updates, Path.Combine(root, RBCONEntry.SONGUPDATES_DTA));
        Assert.That(result.Status, Is.EqualTo(SongScanStatus.Success), result.Message);
        var entries = result.Cache!.Entries.Values.SelectMany(v => v).ToArray();
        Assert.That(entries, Has.Length.EqualTo(2));
        Assert.That(entries.All(e => e.Name.ToString() == "Selected"), Is.True);
        Assert.That(result.AffectedCount, Is.EqualTo(2));
    }

    [Test]
    public void SourceFingerprintsDetectContentChanges()
    {
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        var before = SuccessfulEntry(Explicit(SongScanScope.All, path));
        string midi = Path.Combine(root, NODE, NODE + "_update.mid");
        var time = File.GetLastWriteTimeUtc(midi);
        File.Delete(midi);
        WriteMidi(midi, "PART GUITAR", false);
        File.SetLastWriteTimeUtc(midi, time);
        var after = SuccessfulEntry(Explicit(SongScanScope.All, path, quick: true));
        Assert.That(after[Instrument.FiveFretGuitar].IsActive(), Is.False);
        Assert.That(after.Hash, Is.Not.EqualTo(before.Hash));
    }

    [Test]
    public void CorruptExplicitPayloadRequiresFullScanWithoutTraversal()
    {
        SuccessfulEntry(Explicit(SongScanScope.All));
        byte[] bytes = File.ReadAllBytes(_cachePath);
        bytes[bytes.Length - 1] ^= 1;
        File.WriteAllBytes(_cachePath, bytes);
        var observed = new List<string>();
        CacheHandler.ScanObservation = observed.Add;
        try
        {
            Assert.That(Explicit(SongScanScope.Updates).Status, Is.EqualTo(SongScanStatus.NeedsFullScan));
            Assert.That(observed, Is.Empty);
            Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(bytes));
        }
        finally { CacheHandler.ScanObservation = null; }
    }

    [Test]
    public void ClearedArbitrarySourceRootRemainsExcludedFromBaseDiscovery()
    {
        string root = Path.Combine(_library, "arbitrary");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.dta");
        File.WriteAllText(path, Dta("Selected"));
        string song = Path.Combine(root, NODE);
        Directory.CreateDirectory(song);
        WriteMidi(Path.Combine(song, NODE + "_update.mid"), "PART GUITAR", true);
        // This superficially valid ini must never turn a supplemental asset directory into a base song.
        File.WriteAllText(Path.Combine(song, "song.ini"), "[song]\nname = Supplemental Asset\nsong_length = 1000\n");
        WriteMidi(Path.Combine(song, "notes.mid"), "PART GUITAR", true);
        File.WriteAllBytes(Path.Combine(song, "song.opus"), Array.Empty<byte>());
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.All, path)), "Selected", true);
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.Updates)), "Base", false);
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.Songs)), "Base", false);
    }

    [Test]
    public void DeferredCommitRetainsOldDiskAndDisposeCleansCandidate()
    {
        SuccessfulEntry(Explicit(SongScanScope.All));
        byte[] before = File.ReadAllBytes(_cachePath);
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        using (var result = Explicit(SongScanScope.Updates, path, deferred: true))
        {
            AssertEntry(SuccessfulEntry(result), "Selected", true);
            Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(_root, "*.tmp"), Has.Length.EqualTo(1));
        }
        Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty);
        Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(before));
        using (var result = Explicit(SongScanScope.Updates, path, deferred: true))
        {
            result.Commit();
            result.Commit();
        }
        Assert.That(File.ReadAllBytes(_cachePath), Is.Not.EqualTo(before));
        using var quick = Explicit(SongScanScope.All, path, quick: true, deferred: true);
        quick.Commit();
        Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeferredCommitRevalidatesBeforeReplacingCache(bool mutateBase)
    {
        SuccessfulEntry(Explicit(SongScanScope.All));
        byte[] before = File.ReadAllBytes(_cachePath);
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        using var result = Explicit(SongScanScope.Updates, path, deferred: true);
        SuccessfulEntry(result);
        if (mutateBase) File.SetLastWriteTimeUtc(Path.Combine(_library, "songs", NODE, NODE + ".mid"), DateTime.UtcNow.AddMinutes(2));
        else File.WriteAllText(path, Dta("Changed"));
        Assert.Throws<IOException>(() => result.Commit());
        Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(before));
    }

    [Test]
    public void InvalidBaseIsBadsongsButInvalidSelectedSourceFailsTransaction()
    {
        string directory = Path.Combine(_library, "broken", "songs", "broken");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(directory)!, "songs.dta"), Dta("Broken").Replace(NODE, "broken"));
        File.WriteAllBytes(Path.Combine(directory, "broken.mid"), new byte[] { 0 });
        WriteMogg(Path.Combine(directory, "broken.mogg"), RBCONEntry.UNENCRYPTED_MOGG);
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.All)), "Base", false);
        Assert.That(File.ReadAllText(_badSongsPath), Does.Contain("broken"));
        byte[] before = File.ReadAllBytes(_cachePath);
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        File.WriteAllBytes(Path.Combine(root, NODE, NODE + "_update.mid"), new byte[] { 0 });
        Assert.That(Explicit(SongScanScope.Updates, Path.Combine(root, RBCONEntry.SONGUPDATES_DTA)).Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(before));
        Assert.That(File.ReadAllText(_badSongsPath), Does.Contain("supplemental asset"));
    }

    [Test]
    public void ValidEmptyReplacementKeepsDescriptorAndRemovalRestoresSong()
    {
        string root = AddUpdate("selected", "Empty", "PART BASS", false);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        var empty = Explicit(SongScanScope.All, path);
        Assert.That(empty.Status, Is.EqualTo(SongScanStatus.Success), empty.Message);
        Assert.That(empty.Cache!.Entries, Is.Empty);
        Assert.That(File.ReadAllText(_badSongsPath), Does.Contain("NoNotes"));
        // Reloading an empty effective library must retain inventory, not rediscover base roots.
        AssertEntry(SuccessfulEntry(Explicit(SongScanScope.Updates)), "Base", false);
    }

    [Test]
    public void HundredsOfDescriptorsRetainIniAndSngWithoutBaseTraversal()
    {
        for (int i = 0; i < 300; i++)
        {
            string node = "song" + i;
            string directory = Path.Combine(_library, "many", "songs", node);
            Directory.CreateDirectory(directory);
            WriteMidi(Path.Combine(directory, node + ".mid"), "PART BASS", true);
            WriteMogg(Path.Combine(directory, node + ".mogg"), RBCONEntry.UNENCRYPTED_MOGG);
        }
        File.WriteAllText(Path.Combine(_library, "many", "songs", "songs.dta"),
            string.Join("\n", Enumerable.Range(0, 300).Select(i => Dta("Song " + i).Replace(NODE, "song" + i))));
        string ini = Path.Combine(_library, "ini");
        Directory.CreateDirectory(ini);
        WriteMidi(Path.Combine(ini, "notes.mid"), "PART GUITAR", true);
        File.WriteAllText(Path.Combine(ini, "song.ini"), "[song]\nname = Ini preserved\nsong_length = 1000\n");
        File.WriteAllBytes(Path.Combine(ini, "song.opus"), Array.Empty<byte>());
        YARG.Core.UnitTests.Song.SngScanFixture.Write(Path.Combine(_library, "preserved.sng"), "notes.mid", File.ReadAllBytes(Path.Combine(ini, "notes.mid")));
        var initial = Explicit(SongScanScope.All);
        Assert.That(initial.Status, Is.EqualTo(SongScanStatus.Success), initial.Message);
        Assert.That(initial.Cache!.Entries.Values.Sum(v => v.Count), Is.EqualTo(303));
        File.Delete(Path.Combine(ini, "notes.mid")); // Unrelated cached entries are deliberately trusted.
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        var observed = new List<string>();
        CacheHandler.ScanObservation = observed.Add;
        try
        {
            var result = Explicit(SongScanScope.Updates, Path.Combine(root, RBCONEntry.SONGUPDATES_DTA));
            Assert.That(result.Status, Is.EqualTo(SongScanStatus.Success), result.Message);
            Assert.That(result.Cache!.Entries.Values.Sum(v => v.Count), Is.EqualTo(303));
            Assert.That(result.Cache.Entries.Values.SelectMany(v => v).Any(e => e.Name.ToString() == "Ini preserved"), Is.True);
            Assert.That(observed.Count(p => p.StartsWith("directory:")), Is.Zero);
            Assert.That(observed.Count(p => p.StartsWith("base:")), Is.EqualTo(1));
        }
        finally { CacheHandler.ScanObservation = null; }
    }

    [Test]
    public void RuntimeAssetManifestUsesActualSubnameAndValidUpdateMoggScans()
    {
        const string subname = "actualassets";
        string songs = Path.Combine(_library, "songs");
        string renamed = Path.Combine(songs, subname);
        Directory.Move(Path.Combine(songs, NODE), renamed);
        File.Move(Path.Combine(renamed, NODE + ".mid"), Path.Combine(renamed, subname + ".mid"));
        File.Move(Path.Combine(renamed, NODE + ".mogg"), Path.Combine(renamed, subname + ".mogg"));
        File.WriteAllText(Path.Combine(songs, "songs.dta"), Dta("Base").Replace("songs/" + NODE + "/" + NODE, "songs/" + subname + "/" + subname));
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        string path = Path.Combine(root, RBCONEntry.SONGUPDATES_DTA);
        File.WriteAllText(path, Dta("Selected").Replace("songs/" + NODE + "/" + NODE, "songs/" + subname + "/" + subname));
        string assets = Path.Combine(root, subname);
        Directory.CreateDirectory(Path.Combine(assets, "gen"));
        string mogg = Path.Combine(assets, subname + "_update.mogg");
        WriteMogg(mogg, RBCONEntry.UNENCRYPTED_MOGG);
        var entry = SuccessfulEntry(Explicit(SongScanScope.All, path));
        Assert.That(entry.LoadChart(), Is.Not.Null);
        entry = SuccessfulEntry(Explicit(SongScanScope.All, path, quick: true));
        File.WriteAllBytes(Path.Combine(assets, "gen", subname + "_keep.png_xbox"), new byte[] { 0 });
        // The production asset loader must reject the newly changed dependency before decoding it.
        Assert.That(entry.LoadAlbumData(), Is.Null);
        Assert.That(entry.LoadChart(), Is.Null);
        byte[] before = File.ReadAllBytes(_cachePath);
        Assert.That(Explicit(SongScanScope.Upgrades, path).Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(before));
        File.WriteAllBytes(mogg, new byte[] { 0 });
        Assert.That(Explicit(SongScanScope.Updates, path).Status, Is.EqualTo(SongScanStatus.SourceError));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UpgradeLocationChangeRequiresAppliedUpdateAssetPaths(bool quickReload)
    {
        const string alternate = "alternate";
        string songs = Path.Combine(_library, "songs");
        string directory = Path.Combine(songs, alternate);
        Directory.CreateDirectory(directory);
        WriteMidi(Path.Combine(directory, alternate + ".mid"), "PART BASS", true);
        WriteMogg(Path.Combine(directory, alternate + ".mogg"), RBCONEntry.UNENCRYPTED_MOGG);
        string updates = Path.Combine(_root, "updates");
        Directory.CreateDirectory(updates);
        string update = Path.Combine(updates, "metadata.dta");
        File.WriteAllText(update, "(" + NODE + " (name \"Metadata only\"))");
        string upgrades = Path.Combine(_root, "upgrades");
        Directory.CreateDirectory(upgrades);
        string upgrade = Path.Combine(upgrades, "location.dta");
        File.WriteAllText(upgrade, Dta("Upgrade").Replace("songs/" + NODE + "/" + NODE, "songs/" + alternate + "/" + alternate));
        WriteMidi(Path.Combine(upgrades, NODE + "_plus.mid"), "PART GUITAR", true);
        SuccessfulEntry(Explicit(SongScanScope.All, update));
        if (quickReload) SuccessfulEntry(Explicit(SongScanScope.All, update, quick: true));
        byte[] before = File.ReadAllBytes(_cachePath);
        var rejected = Explicit(SongScanScope.Upgrades, update, upgrade);
        Assert.That(rejected.Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(rejected.Message, Does.Contain("Scan Updates").And.Contain("Scan All"));
        Assert.That(File.ReadAllBytes(_cachePath), Is.EqualTo(before));
        string assets = Path.Combine(updates, alternate, "gen");
        Directory.CreateDirectory(assets);
        string album = Path.Combine(assets, alternate + "_keep.png_xbox");
        // Even an asset introduced after the original applied snapshot cannot be silently adopted.
        File.WriteAllBytes(album, new byte[] { 0 });
        Assert.That(Explicit(SongScanScope.Upgrades, update, upgrade).Status, Is.EqualTo(SongScanStatus.SourceError));
        File.Delete(album);
        var entry = SuccessfulEntry(Explicit(SongScanScope.All, update, upgrade));
        // Once Scan All records these paths, an unchanged location can be applied independently.
        SuccessfulEntry(Explicit(SongScanScope.Upgrades, update, upgrade));
        if (quickReload) entry = SuccessfulEntry(Explicit(SongScanScope.All, update, upgrade, quick: true));
        File.WriteAllBytes(album, new byte[] { 0 });
        Assert.That(entry.LoadAlbumData(), Is.Null);
        Assert.That(entry.LoadChart(), Is.Null);
        Assert.That(Explicit(SongScanScope.Upgrades, update, upgrade).Status, Is.EqualTo(SongScanStatus.SourceError));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TemporaryDeleteFailureDoesNotHideCommittedOrDeferredResult(bool deferred)
    {
        SuccessfulEntry(Explicit(SongScanScope.All));
        byte[] before = File.ReadAllBytes(_cachePath);
        string root = AddUpdate("selected", "Selected", "PART GUITAR", true);
        var attempts = new List<string>();
        CacheHandler.BeforeTemporaryDelete = path => { attempts.Add(path); throw new IOException("Injected delete failure"); };
        SongScanResult? result = null;
        try
        {
            result = Explicit(SongScanScope.Updates, Path.Combine(root, RBCONEntry.SONGUPDATES_DTA), deferred: deferred);
            AssertEntry(SuccessfulEntry(result), "Selected", true);
            Assert.That(attempts, Is.Not.Empty);
            Assert.That(File.ReadAllBytes(_cachePath), deferred ? Is.EqualTo(before) : Is.Not.EqualTo(before));
            Assert.DoesNotThrow(() => result.Dispose());
            int count = attempts.Count;
            Assert.DoesNotThrow(() => result.Dispose());
            Assert.That(attempts.Count, Is.EqualTo(count), "Disposed state must be cleared even when deletion fails.");
            Assert.Throws<ObjectDisposedException>(() => result.Commit());
            if (deferred) Assert.That(Directory.GetFiles(_root, "*.tmp"), Has.Length.EqualTo(2), "Only the OS-rejected payload and candidate may remain.");
            else Assert.That(Directory.GetFiles(_root, "*.tmp"), Has.Length.EqualTo(1), "Only the OS-rejected payload may remain.");
        }
        finally
        {
            CacheHandler.BeforeTemporaryDelete = null;
            result?.Dispose();
            foreach (string path in Directory.GetFiles(_root, "*.tmp")) File.Delete(path);
        }
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
