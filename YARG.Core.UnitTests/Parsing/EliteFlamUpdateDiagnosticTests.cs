using System;
using System.Linq;
using MoonscraperChartEditor.Song;
using MoonscraperChartEditor.Song.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Parsing;

namespace YARG.Core.UnitTests.Parsing;

[TestFixture]
public sealed class EliteFlamUpdateDiagnosticTests
{
    [Test]
    public void InspectLocalUpdateFlams()
    {
        var paths = Environment.GetEnvironmentVariable("YARG_FLAM_DIAGNOSTIC_FILES");
        if (string.IsNullOrEmpty(paths)) Assert.Ignore("Set YARG_FLAM_DIAGNOSTIC_FILES to semicolon-separated local MIDI paths.");
        foreach (var path in paths!.Split(';'))
        {
            var song = MidReader.ReadMidi(path);
            var loader = new MoonSongLoader(song, ParseSettings.Default);
            var track = loader.LoadEliteDrumsTrack(Instrument.EliteDrums);
            foreach (var difficulty in new[] { Difficulty.Hard, Difficulty.Expert })
            {
                var raw = song.GetChart(MoonSong.MoonInstrument.EliteDrums,
                    difficulty == Difficulty.Hard ? MoonSong.Difficulty.Hard : MoonSong.Difficulty.Expert);
                var rawFlams = raw.notes.Where(n => (n.flags & (MoonNote.Flags.EliteDrums_Flam | MoonNote.Flags.EliteDrums_FlatFlam)) != 0).ToArray();
                var loaded = track.GetDifficulty(difficulty);
                var notes = loaded.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
                TestContext.Progress.WriteLine($"FILE {path} {difficulty}: rawFlams={rawFlams.Length} authored={notes.Count(n => n.IsAuthoredFlam)} effective={notes.Count(n => n.IsFlam || n.IsFlatFlam)} lanes={loaded.EliteDrumNativeAuthoredLaneRecords.Count}");
                foreach (var note in rawFlams)
                    TestContext.Progress.WriteLine($"RAW {note.tick} {note.eliteDrumPad} {note.flags}");
                foreach (var note in notes.Where(n => n.IsAuthoredFlam || n.IsFlam || n.IsFlatFlam))
                    TestContext.Progress.WriteLine($"LOADED {note.Tick} {(EliteDrumNote.EliteDrumPad)note.Pad} authored={note.IsAuthoredFlam} effective={note.IsFlam} flat={note.IsFlatFlam}");
            }
        }
    }
}
