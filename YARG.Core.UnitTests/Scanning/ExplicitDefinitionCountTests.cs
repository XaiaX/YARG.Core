using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.Song.Cache;

namespace YARG.Core.UnitTests.Scanning;

[Category("Integration")]
[NonParallelizable]
public sealed class ExplicitDefinitionCountTests
{
    private string _root = null!;
    private string _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "yarg-definition-counts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "songs"));
        _cache = Path.Combine(_root, "cache.bin");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    private SongScanResult Scan(SongScanScope scope, string? updates = null, string? upgrades = null,
        bool quick = false, bool deferred = false) => CacheHandler.RunScan(scope,
        new SongSourceConfiguration(new[] { Path.Combine(_root, "songs") }, updates, upgrades),
        quick, _cache, Path.Combine(_root, "bad.txt"), false, deferred);

    private string Source(string name, string contents, bool upgrades = false)
    {
        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "selected.dta");
        // Give shorthand nodes a normal metadata field; the production reader expects content after the name.
        contents = contents.Replace("(missing)", "(missing (name \"Missing\"))")
            .Replace("(other)", "(other (name \"Other\"))");
        File.WriteAllText(path, contents);
        if (upgrades)
        {
            foreach (string node in new[] { "missing", "other" })
            {
                var midi = new MidiFile(new TrackChunk(), new TrackChunk(new SequenceTrackNameEvent("PART GUITAR")))
                {
                    TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
                };
                midi.Write(Path.Combine(directory, node + "_plus.mid"));
            }
        }
        return path;
    }

    private static void Counts(SongScanResult result, int updates, int upgrades)
    {
        Assert.That(result.Status, Is.EqualTo(SongScanStatus.Success), result.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.UpdatesDefinitionCount, Is.EqualTo(updates));
            Assert.That(result.UpgradesDefinitionCount, Is.EqualTo(upgrades));
        });
    }

    [TestCase(" \t\r\n; leading\n", " \t\n; between\n")]
    [TestCase("\uFEFF", "\n")]
    [TestCase("\uFEFF\n", "\n")]
    [TestCase("; leading\r", "; between\r")]
    public void WhitespaceAndBomSourcesCountBothDefinitions(string prefix, string separator)
    {
        string text = prefix + "(missing)" + separator + "(other)";
        string updates = Source("updates", text);
        string upgrades = Source("upgrades", text, true);
        using var full = Scan(SongScanScope.All, updates, upgrades);
        Counts(full, 2, 2);
        string updatePackage = Path.Combine(_root, "updates.yargup");
        string upgradePackage = Path.Combine(_root, "upgrades.yargup");
        YARG.Core.Song.Supplements.SupplementPackageBuilder.Pack(updates,
            YARG.Core.Song.Supplements.SupplementSourceKind.Updates, updatePackage);
        YARG.Core.Song.Supplements.SupplementPackageBuilder.Pack(upgrades,
            YARG.Core.Song.Supplements.SupplementSourceKind.Upgrades, upgradePackage);
        using var packaged = Scan(SongScanScope.All, updatePackage, upgradePackage);
        Counts(packaged, 2, 2);
        using var quick = Scan(SongScanScope.All, updatePackage, upgradePackage, quick: true);
        Counts(quick, 2, 2);
    }

    [Test]
    public void FullAndQuickCountEveryUnmatchedDefinitionIncludingRepeatedNodes()
    {
        string updates = Source("updates", "(missing (name \"One\"))\n(missing (name \"Two\"))\n(other (name \"Three\"))");
        string upgrades = Source("upgrades", "(missing)\n(missing)\n(other)", true);
        using var full = Scan(SongScanScope.All, updates, upgrades);
        Counts(full, 3, 3);
        Assert.That(full.Cache!.Entries, Is.Empty);
        using var quick = Scan(SongScanScope.All, updates, upgrades, quick: true);
        Counts(quick, 3, 3);
        Assert.That(quick.Cache!.Entries, Is.Empty);
    }

    [TestCase(SongScanScope.Updates)]
    [TestCase(SongScanScope.Upgrades)]
    public void PartialCountsOnlyRequestedSourceAndRetainsOtherAppliedCount(SongScanScope scope)
    {
        string updates = Source("updates", "(missing)");
        string upgrades = Source("upgrades", "(missing)\n(other)", true);
        using (var full = Scan(SongScanScope.All, updates, upgrades)) Counts(full, 1, 2);
        string replacement = Source("replacement", "(missing)\n(missing)\n(other)", scope == SongScanScope.Upgrades);
        string pendingInvalid = Path.Combine(_root, "not-selected.dta");
        using var partial = scope == SongScanScope.Updates
            ? Scan(scope, replacement, pendingInvalid)
            : Scan(scope, pendingInvalid, replacement);
        Counts(partial, scope == SongScanScope.Updates ? 3 : 1, scope == SongScanScope.Upgrades ? 3 : 2);
        Assert.That(partial.AppliedSources!.UpdatesDtaPath, Is.EqualTo(scope == SongScanScope.Updates ? replacement : updates));
        Assert.That(partial.AppliedSources.UpgradesDtaPath, Is.EqualTo(scope == SongScanScope.Upgrades ? replacement : upgrades));
        using var quick = Scan(SongScanScope.All, partial.AppliedSources.UpdatesDtaPath,
            partial.AppliedSources.UpgradesDtaPath, quick: true);
        Counts(quick, partial.UpdatesDefinitionCount!.Value, partial.UpgradesDefinitionCount!.Value);
    }

    [TestCase("")]
    [TestCase("; valid empty source\n  \n")]
    [TestCase("\uFEFF")]
    [TestCase("\uFEFF; empty source\r \t")]
    public void EmptyAndClearedSourcesReportZeroThroughFullPartialAndQuick(string contents)
    {
        string updates = Source("updates", contents);
        string upgrades = Source("upgrades", contents);
        using (var full = Scan(SongScanScope.All, updates, upgrades)) Counts(full, 0, 0);
        using (var quick = Scan(SongScanScope.All, updates, upgrades, quick: true)) Counts(quick, 0, 0);
        using (var partial = Scan(SongScanScope.Updates, null, upgrades)) Counts(partial, 0, 0);
        using (var partial = Scan(SongScanScope.Upgrades)) Counts(partial, 0, 0);
        using (var quick = Scan(SongScanScope.All, quick: true)) Counts(quick, 0, 0);
    }

    [Test]
    public void ClearingPreviouslyCountedSourcesResetsOnlyRequestedSection()
    {
        string updates = Source("updates", "(missing)");
        string upgrades = Source("upgrades", "(missing)\n(other)", true);
        using (var full = Scan(SongScanScope.All, updates, upgrades)) Counts(full, 1, 2);
        using (var partial = Scan(SongScanScope.Updates, null, upgrades)) Counts(partial, 0, 2);
        using (var quick = Scan(SongScanScope.All, null, upgrades, quick: true)) Counts(quick, 0, 2);
        using (var partial = Scan(SongScanScope.Upgrades)) Counts(partial, 0, 0);
        using (var quick = Scan(SongScanScope.All, quick: true)) Counts(quick, 0, 0);
    }

    [TestCase(SongScanScope.All)]
    [TestCase(SongScanScope.Updates)]
    [TestCase(SongScanScope.Upgrades)]
    public void FailedScanDoesNotReplacePersistedCounts(SongScanScope scope)
    {
        string updates = Source("updates", "(missing)");
        string upgrades = Source("upgrades", "(missing)\n(other)", true);
        using (var full = Scan(SongScanScope.All, updates, upgrades)) Counts(full, 1, 2);
        byte[] previous = File.ReadAllBytes(_cache);
        string invalid = Source("invalid", "(broken");
        using var failed = scope == SongScanScope.Upgrades
            ? Scan(scope, updates, invalid)
            : Scan(scope, invalid, upgrades);
        Assert.Multiple(() =>
        {
            Assert.That(failed.Status, Is.EqualTo(SongScanStatus.SourceError));
            Assert.That(failed.UpdatesDefinitionCount, Is.Null);
            Assert.That(failed.UpgradesDefinitionCount, Is.Null);
            Assert.That(File.ReadAllBytes(_cache), Is.EqualTo(previous));
        });
        using var quick = Scan(SongScanScope.All, updates, upgrades, quick: true);
        Counts(quick, 1, 2);
    }

    [Test]
    public void RejectedDeferredCommitRetainsPreviouslyPersistedCounts()
    {
        string updates = Source("updates", "(missing)");
        using (var full = Scan(SongScanScope.All, updates)) Counts(full, 1, 0);
        byte[] previous = File.ReadAllBytes(_cache);
        string replacement = Source("replacement", "(missing)\n(other)");
        using var candidate = Scan(SongScanScope.Updates, replacement, deferred: true);
        Counts(candidate, 2, 0);
        File.WriteAllText(replacement, "(changed)");
        Assert.Throws<IOException>(() => candidate.Commit());
        Assert.That(File.ReadAllBytes(_cache), Is.EqualTo(previous));
        using var quick = Scan(SongScanScope.All, updates, quick: true);
        Counts(quick, 1, 0);
    }
    [Test]
    public void QuickStartupUsesSnapshotWithoutOpeningChangedSupplementalFiles()
    {
        string upgrades = Source("upgrades", "(missing)", true);
        using (var full = Scan(SongScanScope.All, upgrades: upgrades)) Counts(full, 0, 1);
        byte[] cache = File.ReadAllBytes(_cache);
        File.WriteAllText(upgrades, "not a valid DTA");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(upgrades)!, "missing_plus.mid"), "not MIDI");
        using (var quick = Scan(SongScanScope.All, upgrades: upgrades, quick: true)) Counts(quick, 0, 1);
        Assert.That(File.ReadAllBytes(_cache), Is.EqualTo(cache));
        using var explicitScan = Scan(SongScanScope.All, upgrades: upgrades);
        Assert.That(explicitScan.Status, Is.EqualTo(SongScanStatus.SourceError));
        Assert.That(File.ReadAllBytes(_cache), Is.EqualTo(cache));
    }

    [Test]
    public void ExplicitScanReportsSourceValidationBeforeReadingMidi()
    {
        string upgrades = Source("upgrades", "(missing)", true);
        bool reported = false;
        CacheHandler.ScanObservation = operation =>
        {
            if (operation == "source-validation:" + Path.Combine(Path.GetDirectoryName(upgrades)!, "missing_plus.mid"))
            {
                reported = true;
                var progress = CacheHandler.SourceValidation;
                Assert.That(progress, Is.Not.Null);
                Assert.That(progress!.File, Is.EqualTo(Path.Combine(Path.GetDirectoryName(upgrades)!, "missing_plus.mid")));
                Assert.That(progress.Total, Is.GreaterThan(0));
                Assert.That(progress.Completed, Is.InRange(0, progress.Total));
            }
        };
        try
        {
            using var scan = Scan(SongScanScope.All, upgrades: upgrades);
            Counts(scan, 0, 1);
            Assert.That(reported, Is.True, "MIDI validation must expose progress before doing file work.");
            Assert.That(CacheHandler.SourceValidation, Is.Null, "Completed scans must clear validation feedback.");
        }
        finally { CacheHandler.ScanObservation = null; }
    }

    [Test]
    public void LocalStartupCacheBenchmark()
    {
        string? path = Environment.GetEnvironmentVariable("YARG_STARTUP_CACHE");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set YARG_STARTUP_CACHE to benchmark a copy of a local cache.");
        File.Copy(path!, _cache);
        using var reader = new BinaryReader(File.OpenRead(_cache));
        Assert.That(reader.ReadString(), Is.EqualTo("YARG explicit sources"));
        reader.ReadInt32();
        var inventory = JObject.Parse(reader.ReadString());
        var sources = new SongSourceConfiguration(inventory["Roots"]!.Values<string>().Select(root => root!),
            (string?)inventory["Updates"]!["Path"], (string?)inventory["Upgrades"]!["Path"]);
        reader.Close();
        CacheHandler.ScanObservation = operation => Assert.Fail("Startup performed scan work: " + operation);
        try
        {
            var timer = Stopwatch.StartNew();
            using var result = CacheHandler.RunScan(SongScanScope.All, sources, true, _cache,
                Path.Combine(_root, "bad.txt"), (bool)inventory["FullPlaylists"]!);
            Assert.That(result.Status, Is.EqualTo(SongScanStatus.Success), result.Message);
            TestContext.Out.WriteLine($"Startup cache: {timer.Elapsed.TotalSeconds:F3}s, " +
                $"{result.Cache!.Entries.Values.Sum(entries => entries.Count)} songs; no source validation or discovery.");
        }
        finally { CacheHandler.ScanObservation = null; }
    }

}
