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

[NonParallelizable]
public sealed class LibraryRefreshReviewReproTests
{
    private const string NODE = "reviewrepro";
    private string _root = null!;
    private string _library = null!;
    private string _cachePath = null!;
    private string _badSongsPath = null!;
    private bool _cumulativeUpdates;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "yarg-library-refresh-repro-" + Guid.NewGuid().ToString("N"));
        _library = Path.Combine(_root, "library");
        _cachePath = Path.Combine(_root, "songcache.bin");
        _badSongsPath = Path.Combine(_root, "badsongs.txt");

        string songs = Path.Combine(_library, "songs");
        string song = Path.Combine(songs, NODE);
        Directory.CreateDirectory(song);
        File.WriteAllText(Path.Combine(songs, "songs.dta"), Dta("Base"));
        WriteMidi(Path.Combine(song, NODE + ".mid"));
        WriteMogg(Path.Combine(song, NODE + ".mogg"));

        string updateSong = Path.Combine(_library, "package", "songs_updates", NODE);
        Directory.CreateDirectory(updateSong);
        string updateDirectory = Directory.GetParent(updateSong)!.FullName;
        File.WriteAllText(Path.Combine(updateDirectory, RBCONEntry.SONGUPDATES_DTA), Dta("Before"));
        WriteMidi(Path.Combine(updateSong, NODE + "_update.mid"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    // A changed update must apply on the first refresh in both default and layered modes.
    [TestCase(false)]
    [TestCase(true)]
    public void ChangedUpdateDta_IsAppliedOnFirstFullScanAndRetainedOnSecond(bool cumulativeUpdates)
    {
        _cumulativeUpdates = cumulativeUpdates;
        string updateDta = Path.Combine(_library, "package", "songs_updates", RBCONEntry.SONGUPDATES_DTA);

        var initial = ScanFull();
        Assert.That(initial.Name.ToString(), Is.EqualTo("Before"));

        File.WriteAllText(updateDta, Dta("After"));
        File.SetLastWriteTimeUtc(updateDta, DateTime.UtcNow.AddMinutes(5));
        var updateInfo = new FileInfo(updateDta);
        Console.WriteLine($"Changed DTA path: {updateDta}");
        Console.WriteLine($"Changed DTA UTC last-write: {updateInfo.LastWriteTimeUtc:O}");
        Console.WriteLine($"Changed DTA bytes: {File.ReadAllText(updateDta).Length}");

        string firstFullScanName = ScanFull().Name.ToString();
        Console.WriteLine($"First full scan metadata name: {firstFullScanName}");
        string secondFullScanName = ScanFull().Name.ToString();
        Console.WriteLine($"Second full scan metadata name: {secondFullScanName}");

        Assert.Multiple(() =>
        {
            Assert.That(firstFullScanName, Is.EqualTo("After"),
                "The first full scan after the timestamp/content change must use the changed update.");
            Assert.That(secondFullScanName, Is.EqualTo("After"),
                "The following full scan must retain the same changed update.");
        });
    }

    private SongCache ScanAll() => CacheHandler.RunScan(false, _cachePath, _badSongsPath,
        false, new List<string> { _library }, _cumulativeUpdates);

    private RBCONEntry ScanFull()
    {
        var entries = ScanAll().Entries.Values.SelectMany(entries => entries).ToArray();
        Assert.That(entries, Has.Length.EqualTo(1),
            $"Expected exactly one unpacked RBCON. Bad songs: {(File.Exists(_badSongsPath) ? File.ReadAllText(_badSongsPath) : "none")}");
        Assert.That(entries[0], Is.InstanceOf<RBCONEntry>());
        return (RBCONEntry) entries[0];
    }

    private static string Dta(string title) => $$"""
        ({{NODE}}
          (name "{{title}}")
          (song_length 60000)
          (song
            (name "songs/{{NODE}}/{{NODE}}")
            (pans (0.0))
            (vols (0.0))
            (cores (0.0))
          )
        )
        """;

    private static void WriteMidi(string path)
    {
        var midi = new MidiFile(new TrackChunk())
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };
        var bass = new TrackChunk(new SequenceTrackNameEvent("PART BASS"),
            new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) 96,
                (Melanchall.DryWetMidi.Common.SevenBitNumber) 100) { DeltaTime = 1 },
            new NoteOffEvent((Melanchall.DryWetMidi.Common.SevenBitNumber) 96,
                (Melanchall.DryWetMidi.Common.SevenBitNumber) 0) { DeltaTime = 120 });
        midi.Chunks.Add(bass);
        midi.Write(path);
    }

    private static void WriteMogg(string path)
    {
        using var output = File.Create(path);
        output.Write(RBCONEntry.UNENCRYPTED_MOGG, Endianness.Little);
    }
}
