using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

/// <summary>
/// Matched physical charts and strike timestamps, not an assertion that the lane rules are equivalent:
/// Pro has one alternating trill; native Elite has two independently refreshed authored pad lanes.
/// Single-pad charts retain the same total gem count and temporal density as the alternating charts.
/// </summary>
public sealed class DrumLaneCadenceCharacterizationTests
{
    private const int NOTE_COUNT = 96;
    private const int RESOLUTION = 480;
    private const double START = 1.0;
    private static readonly float[] Thresholds = { .06f, .12f, .2f, .45f, .75f, 1.09f };

    public enum Pattern { ProTrill, NativeEliteTwoPad, ProTremolo, NativeEliteOnePad }

    [TestCase(120, .160)]
    [TestCase(120, .200)]
    [TestCase(180, .160)]
    [TestCase(180, .200)]
    public void SlowingStrikeSchedule_CharacterizesFirstComboBreak(int bpm, double laneWindow)
    {
        // Integer microseconds avoid accumulated floating-point drift. Start at 40 strikes/sec,
        // then add 10ms to each successive inter-strike interval, up to 525ms.
        var times = StrikeTimes(START + NOTE_COUNT * 60.0 / bpm / 4, null);
        var results = new Dictionary<Pattern, Outcome>();
        foreach (var pattern in Enum.GetValues<Pattern>())
        {
            var first = Run(pattern, bpm, laneWindow, times);
            var repeated = Run(pattern, bpm, laneWindow, times);
            Assert.That(repeated, Is.EqualTo(first), "fresh engines must reproduce the exact break timestamp and counters");
            Assert.That(first.BreakTime, Is.Not.Null, "the slowing schedule must eventually break an established combo");
            Assert.That(first.HitsBeforeBreak, Is.GreaterThanOrEqualTo(1), "a break must follow an established combo");
            results.Add(pattern, first);
            Report(pattern, bpm, laneWindow, times, first);
            Assert.That(first.BreakTime, Is.EqualTo(ExpectedRampBreak(pattern, bpm, laneWindow)).Within(1e-12),
                "characterized current engine result; this is not a cross-instrument parity requirement");
        }
        TestContext.Out.WriteLine($"Two-pad Elite minus Pro first break: {results[Pattern.NativeEliteTwoPad].BreakTime - results[Pattern.ProTrill].BreakTime:R}s");
        TestContext.Out.WriteLine($"Single-pad Elite minus Pro first break: {results[Pattern.NativeEliteOnePad].BreakTime - results[Pattern.ProTremolo].BreakTime:R}s");
    }

    [TestCase(120, .160)]
    [TestCase(120, .200)]
    [TestCase(180, .160)]
    [TestCase(180, .200)]
    public void ConstantStrikeRates_ReportFirstBreakWithoutAssumingParity(int bpm, double laneWindow)
    {
        foreach (int intervalUs in new[] { 25000, 50000, 75000, 100000, 125000, 150000, 200000 })
        {
            var times = StrikeTimes(START + NOTE_COUNT * 60.0 / bpm / 4, intervalUs);
            foreach (var pattern in Enum.GetValues<Pattern>())
            {
                var result = Run(pattern, bpm, laneWindow, times);
                Assert.That(Run(pattern, bpm, laneWindow, times), Is.EqualTo(result));
                Report(pattern, bpm, laneWindow, times, result);
            }
        }
    }

    private static double ExpectedRampBreak(Pattern pattern, int bpm, double window) => (bpm, window, pattern) switch
    {
        (120, .160, Pattern.NativeEliteTwoPad) => 2.695,
        (120, .200, Pattern.NativeEliteTwoPad) => 2.695,
        (120, .160, Pattern.NativeEliteOnePad) => 5.570,
        (120, .160, _) => 3.945,
        (120, .200, _) => 6.195,
        (180, .160, Pattern.NativeEliteTwoPad) => 1.9033333333333338,
        (180, .200, Pattern.NativeEliteTwoPad) => 2.320,
        (180, .200, Pattern.NativeEliteOnePad) => 6.153333333333334,
        (180, _, _) => 5.236666666666668,
        _ => throw new ArgumentOutOfRangeException(nameof(bpm))
    };

    private static List<double> StrikeTimes(double end, int? constantIntervalUs)
    {
        // Enter both alternating pads on their first authored gems before testing continuation.
        // The one-pad patterns receive these same two timestamps, not an easier entry schedule.
        double rampStart = START + (end - START) / NOTE_COUNT;
        var times = new List<double> { START, rampStart };
        long elapsedUs = 0;
        for (int strike = 0; ; strike++)
        {
            elapsedUs += constantIntervalUs ?? Math.Min(525000, 25000 + strike * 10000);
            double time = rampStart + elapsedUs / 1000000.0;
            if (time >= end) break;
            times.Add(time);
        }
        return times;
    }

    private static void Report(Pattern pattern, int bpm, double window, List<double> times, Outcome result)
    {
        bool twoPad = pattern is Pattern.ProTrill or Pattern.NativeEliteTwoPad;
        int preceding = result.BreakTime.HasValue ? times.FindLastIndex(t => t <= result.BreakTime.Value) : times.Count - 1;
        double? interval = preceding > 0 ? times[preceding] - times[preceding - 1] : null;
        double? samePad = preceding >= (twoPad ? 2 : 1) ? times[preceding] - times[preceding - (twoPad ? 2 : 1)] : null;
        double? nextInterval = preceding + 1 < times.Count ? times[preceding + 1] - times[preceding] : null;
        TestContext.Out.WriteLine($"{pattern}: bpm={bpm}, notes={NOTE_COUNT}, gemInterval={60.0 / bpm / 4:R}s, laneWindow={window:R}s; " +
            $"firstBreak={result.BreakTime?.ToString("R") ?? "none"}, hitsBeforeBreak={result.HitsBeforeBreak}, " +
            $"precedingStrike={preceding}, strikeTime={(preceding >= 0 ? times[preceding].ToString("R") : "none")}, " +
            $"aggregateInterval={interval?.ToString("R")}, aggregateHz={(1 / interval)?.ToString("R")}, " +
            $"samePadInterval={samePad?.ToString("R")}, nextInterval={nextInterval?.ToString("R")}, " +
            $"finalHits={result.FinalHits}, misses={result.FinalMisses}, overhits={result.Overhits}");
    }

    private static Outcome Run(Pattern pattern, int bpm, double laneWindow, List<double> times)
    {
        bool elite = pattern is Pattern.NativeEliteTwoPad or Pattern.NativeEliteOnePad;
        bool twoPad = pattern is Pattern.ProTrill or Pattern.NativeEliteTwoPad;
        var sync = new SyncTrack(RESOLUTION);
        sync.Tempos.Add(new TempoChange(bpm, 0, 0));
        // Deliberately synthetic, fixed 140ms total hit window (not a full named preset).
        // Lane windows cover the default .160 and the alternative .200 preset values.
        var hitWindow = new HitWindowSettings(.14, .14, 1, false, 0, 1, 1, laneWindow, .25);
        var parameters = new DrumsEngineParameters(hitWindow, 4, Thresholds, Thresholds,
            elite ? DrumsEngineParameters.DrumMode.Elite : DrumsEngineParameters.DrumMode.ProFourLane,
            false, true);
        var proNotes = new List<DrumNote>();
        var eliteNotes = new List<EliteDrumNote>();
        for (int i = 0; i < NOTE_COUNT; i++)
        {
            double time = START + i * 60.0 / bpm / 4;
            uint tick = (uint) Math.Round(time * bpm / 60 * RESOLUTION);
            int padIndex = twoPad ? i % 2 : 0;
            var flags = elite || !twoPad ? NoteFlags.Tremolo : NoteFlags.Trill;
            if (i == 0 || (elite && twoPad && i == 1)) flags |= NoteFlags.LaneStart;
            if (i == NOTE_COUNT - 1 || (elite && twoPad && i == NOTE_COUNT - 2)) flags |= NoteFlags.LaneEnd;
            proNotes.Add(new DrumNote(padIndex == 0 ? FourLaneDrumPad.YellowCymbal : FourLaneDrumPad.BlueCymbal,
                DrumNoteType.Neutral, DrumNoteFlags.None, flags, time, tick, false));
            var pad = padIndex == 0 ? EliteDrumNote.EliteDrumPad.LeftCrash : EliteDrumNote.EliteDrumPad.Ride;
            var source = new EliteDrumSourceDefinition($"cadence-{i}", i, (int) pad, tick, 0, time);
            eliteNotes.Add(new EliteDrumNote(pad, DrumNoteType.Neutral,
                EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                false, DrumNoteFlags.None, flags, EliteDrumNote.EliteDrumsChannelFlag.None,
                time, tick, false, source));
        }
        for (int i = 1; i < NOTE_COUNT; i++)
        {
            proNotes[i - 1].NextNote = proNotes[i];
            proNotes[i].PreviousNote = proNotes[i - 1];
            eliteNotes[i - 1].NextNote = eliteNotes[i];
            eliteNotes[i].PreviousNote = eliteNotes[i - 1];
        }
        var proChart = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert, proNotes, new(), new());
        var eliteChart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert, eliteNotes, new(), new());
        var records = new List<EliteDrumNativeAuthoredLaneRecord>();
        for (int lane = 0; lane < (twoPad ? 2 : 1); lane++)
        {
            var members = eliteNotes.Where((_, i) => !twoPad || i % 2 == lane).ToList();
            var type = lane == 0 ? PhraseType.EliteDrums_LeftCrashLane : PhraseType.EliteDrums_RideLane;
            uint startTick = members[0].Tick;
            uint endTick = members[^1].Tick + RESOLUTION / 4;
            eliteChart.Phrases.Add(new Phrase(type, members[0].Time,
                (endTick - startTick) * 60.0 / bpm / RESOLUTION, startTick, endTick - startTick));
            records.Add(new EliteDrumNativeAuthoredLaneRecord(lane, type, startTick, endTick,
                members.Select(n => n.SourceDefinition!)));
        }
        eliteChart.SetEliteDrumNativeAuthoredLaneRecords(records);
        Assert.That(eliteChart.EliteDrumNativeAuthoredLaneRecords.Sum(r => r.MemberSources.Count), Is.EqualTo(NOTE_COUNT));
        BaseEngine engine;
        Func<int> hits;
        Func<int> misses;
        Func<int> overhits;
        if (elite)
        {
            var native = new EliteDrumsEngine(eliteChart, sync, parameters, false, true);
            engine = native;
            hits = () => native.EngineStats.NotesHit;
            misses = () => native.EngineStats.NotesMissed;
            overhits = () => native.EngineStats.Overhits;
        }
        else
        {
            var pro = new YargDrumsEngine(proChart, sync, parameters, false, false);
            engine = pro;
            hits = () => pro.EngineStats.NotesHit;
            misses = () => pro.EngineStats.NotesMissed;
            overhits = () => pro.EngineStats.Overhits;
        }
        double? breakTime = null;
        int hitsBeforeBreak = 0;
        bool established = false;
        engine.OnComboIncrement += _ => established = true;
        engine.OnComboReset += () =>
        {
            if (!established || breakTime.HasValue) return;
            breakTime = engine.CurrentTime;
            hitsBeforeBreak = hits();
        };
        engine.Update(START - .5);
        for (int i = 0; i < times.Count; i++)
        {
            int padIndex = twoPad ? i % 2 : 0;
            var input = elite
                ? GameInput.Create(times[i], padIndex == 0 ? EliteDrumsAction.EliteLeftCrash : EliteDrumsAction.EliteRide, 1f)
                : GameInput.Create(times[i], padIndex == 0 ? DrumsAction.YellowCymbal : DrumsAction.BlueCymbal, 1f);
            engine.QueueInput(ref input);
            engine.Update(times[i]);
        }
        engine.Update(START + NOTE_COUNT * 60.0 / bpm / 4 + 1);
        Assert.That(hits() + misses(), Is.EqualTo(NOTE_COUNT), "all physical gems must reach a terminal outcome");
        return new Outcome(breakTime, hitsBeforeBreak, hits(), misses(), overhits());
    }

    private sealed record Outcome(double? BreakTime, int HitsBeforeBreak, int FinalHits, int FinalMisses, int Overhits);
}
