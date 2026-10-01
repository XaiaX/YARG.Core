using System;
using System.IO;
using System.Linq;
using MoonscraperChartEditor.Song;
using MoonscraperChartEditor.Song.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Parsing;
using static MoonscraperChartEditor.Song.MoonNote;
using static MoonscraperChartEditor.Song.MoonSong;
using GamePad = YARG.Core.Chart.EliteDrumNote.EliteDrumPad;
using MoonPad = MoonscraperChartEditor.Song.MoonNote.EliteDrumPad;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Parsing;

[TestFixture]
[Explicit("Local real-MIDI reproduction; requires supplied song edit files")]
public sealed class EliteDrumsRealMidiReproTests
{
    private const string PERFECT_DRUG = "/Users/xaiax/Local/song_edits/Updates2/songs_updates/perfectdrug/perfectdrug_update.mid";
    private const string FOREPLAY = "/Users/xaiax/Local/song_edits/Updates2/songs_updates/foreplaylongtime/foreplaylongtime_update.mid";

    [Test]
    public void PerfectDrug_InspectReportedExpertMeasures()
    {
        Inspect(PERFECT_DRUG, new[] { 14.2, 126.2, 131.0, 133.35, 142.3 });
    }

    [Test]
    public void Foreplay_InspectOpeningTriplets()
    {
        Inspect(FOREPLAY, new[] { 15.0, 16.0, 17.0, 18.0 });
    }

    private static void Inspect(string path, double[] measures)
    {
        Assert.That(File.Exists(path), Is.True, $"Missing MIDI: {path}");
        var moonSong = MidReader.ReadMidi(path);
        var loader = new MoonSongLoader(moonSong, ParseSettings.Default);
        var loaded = loader.LoadEliteDrumsTrack(Instrument.EliteDrums).GetDifficulty(Difficulty.Expert);
        var rawChart = moonSong.GetChart(MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);

        TestContext.Progress.WriteLine($"FILE={path} resolution={moonSong.resolution}");
        foreach (var signature in moonSong.syncTrack.TimeSignatures)
            TestContext.Progress.WriteLine($"TIMESIG tick={signature.Tick} {signature.Numerator}/{signature.Denominator}");
        foreach (var text in moonSong.events.Where(text => text.text.Contains("strict", StringComparison.OrdinalIgnoreCase)))
            TestContext.Progress.WriteLine($"TEXT tick={text.tick} {text.text}");

        foreach (double measure in measures)
        {
            int target = MeasureToTick(moonSong, measure);
            TestContext.Progress.WriteLine($"TARGET measure={measure} tick={target}");
            var raw = rawChart.notes.Where(note => Math.Abs((long) note.tick - target) <= moonSong.resolution * 2)
                .OrderBy(note => note.tick).ThenBy(note => note.rawNote).ToArray();
            if (path == PERFECT_DRUG && Math.Abs(measure - 142.3) < 0.0001)
            {
                var rawTargetHat = rawChart.notes.Single(note => note.tick == target && note.eliteDrumPad is MoonPad.HiHat);
                Assert.That(rawTargetHat.flags & Flags.EliteDrums_ForcedClosed, Is.EqualTo(Flags.None),
                    "The authored Expert open hat at measure 142.3 must not be closed by another difficulty's pedal sustain.");
            }
            foreach (var note in raw)
                TestContext.Progress.WriteLine($"RAW tick={note.tick} len={note.length} raw={note.rawNote} pad={note.eliteDrumPad} flags={note.flags} chord={note.isChord}");

            var final = loaded.Notes.SelectMany(note => note.ChildNotes.Prepend(note))
                .Where(note => Math.Abs((long) note.Tick - target) <= moonSong.resolution * 2)
                .OrderBy(note => note.Tick).ThenBy(note => note.Pad).ToArray();
            if (path == PERFECT_DRUG && Math.Abs(measure - 142.3) < 0.0001)
            {
                var targetHat = final.Single(note => note.Tick == (uint) target && note.Pad == (int) GamePad.HiHat);
                Assert.That(targetHat.HatState, Is.EqualTo(EliteDrumsHatState.Open),
                    "The authored Expert open hat at measure 142.3 must remain open.");
            }
            foreach (var note in final)
                TestContext.Progress.WriteLine($"FINAL tick={note.Tick} len={note.TickLength} time={note.Time:F3} pad={(GamePad) note.Pad} pedal={note.HatPedalType} invisible={note.IsInvisibleTerminator} hat={note.HatState} parent={(note.Parent == null ? "-" : ((GamePad) note.Parent.Pad).ToString())} children={string.Join(",", note.ChildNotes.Select(child => $"{(GamePad) child.Pad}:{child.HatPedalType}:{child.HatState}"))} flags={note.Flags}");
        }
    }

    private static int MeasureToTick(MoonSong song, double measure)
    {
        // 1-based measure, with measure.beat subdivision encoded fractionally for
        // quarter-beat increments (e.g. 14.2 => bar 14, beat 2; 133.3.5 => bar
        // 133, beat 3.5 is represented by 133.35 by the caller).
        int resolution = (int) song.resolution;
        int tick = 0;
        var signatures = song.syncTrack.TimeSignatures.OrderBy(signature => signature.Tick).ToArray();
        int targetMeasure = (int) Math.Floor(measure);
        double beat = Math.Abs(measure - targetMeasure) < 0.0001 ? 1 : (measure - targetMeasure) * 10;
        if (targetMeasure < 1) targetMeasure = 1;
        int currentMeasure = 1;
        int sigIndex = 0;
        while (currentMeasure < targetMeasure)
        {
            while (sigIndex + 1 < signatures.Length && (int) signatures[sigIndex + 1].Tick <= tick)
                sigIndex++;
            int numerator = sigIndex < signatures.Length ? (int) signatures[sigIndex].Numerator : 4;
            int denominator = sigIndex < signatures.Length ? (int) signatures[sigIndex].Denominator : 4;
            int barTicks = resolution * numerator * 4 / denominator;
            int nextSigTick = sigIndex + 1 < signatures.Length ? (int) signatures[sigIndex + 1].Tick : int.MaxValue;
            if (tick + barTicks > nextSigTick)
            {
                tick = nextSigTick;
                sigIndex++;
            }
            else
            {
                tick += barTicks;
                currentMeasure++;
            }
        }
        while (sigIndex + 1 < signatures.Length && (int) signatures[sigIndex + 1].Tick <= tick)
            sigIndex++;
        int currentDenominator = sigIndex < signatures.Length ? (int) signatures[sigIndex].Denominator : 4;
        double beatTicks = resolution * 4.0 / currentDenominator;
        return tick + (int) Math.Round((beat - 1) * beatTicks);
    }
}
