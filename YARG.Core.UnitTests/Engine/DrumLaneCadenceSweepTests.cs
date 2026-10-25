using System;
using System.Collections.Generic;
using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;
using YARG.Core.Parsing;
using Pad = YARG.Core.Chart.EliteDrumNote.EliteDrumPad;

namespace YARG.Core.UnitTests.Engine;

/// <summary>Behavioral measurements, deliberately not assertions of engine parity.</summary>
public sealed class DrumLaneCadenceSweepTests
{
    private const int COUNT = 192;
    private const double STEP = .125; // Sixteenths at 120 BPM.
    private const double START = 1;
    private const int WARMUP = 16;
    private static readonly float[] Thresholds = { .06f, .12f, .2f, .45f, .75f, 1.09f };
    public enum Scenario { AB, ABAC, FourHandsAndKick, BonhamABK, AllWithHat, AllWithPedal }
    public enum Playback { NativeElite, ConvertedPro, ClassicPro }
    private sealed record Strike(double Time, Pad Pad);
    private sealed record MissGroup(int Count, double? FirstJudgment, double? FirstNote, string? FirstPad,
        double? PreviousStrike, double? NextStrike);
    private sealed record Outcome(double? FirstBreak, double? FirstMiss, double? FirstOverhit,
        int Hits, int Misses, int Overhits, int Total, int InLaneOverhits, string Judgments, MissGroup Hands, MissGroup Kicks, MissGroup Pedals);

    private static Pad[] Pattern(Scenario scenario) => scenario switch
    {
        Scenario.AB => new[] { Pad.RightCrash, Pad.Ride },
        Scenario.ABAC => new[] { Pad.Tom1, Pad.Tom2, Pad.Tom1, Pad.Tom3 },
        Scenario.FourHandsAndKick => new[] { Pad.Snare, Pad.Tom1, Pad.Tom2, Pad.Tom3 },
        Scenario.BonhamABK => new[] { Pad.Tom1, Pad.Tom2, Pad.Kick },
        _ => new[] { Pad.Snare, Pad.Tom1, Pad.Tom2, Pad.Tom3, Pad.LeftCrash, Pad.RightCrash,
            Pad.Ride }.Concat(scenario == Scenario.AllWithHat ? new[] { Pad.HiHat } : Array.Empty<Pad>()).ToArray()
    };

    private static Pad[] At(Scenario scenario, int index)
    {
        var pattern = Pattern(scenario);
        if (scenario == Scenario.FourHandsAndKick && index % 4 == 0)
            return new[] { pattern[index % pattern.Length], Pad.Kick };
        return new[] { pattern[index % pattern.Length] };
    }

    // Exact first 16 chart events enter every lane. Thereafter instantaneous event cadence
    // decreases linearly in elapsed time from 2x to 0.25x, followed by four seconds at 0.25x.
    // Doubles change only pad selection (ABAB -> AABB, ABAC -> AABBAACC), never timestamps.
    private static List<Strike> Schedule(Scenario scenario, bool doubles, bool literal = false)
    {
        var result = new List<Strike>();
        if (literal)
        {
            for (int i = 0; i < COUNT; i++)
                result.AddRange(At(scenario, i).Select(p => new Strike(START + i * STEP, p)));
            return AddIndependentFeet(scenario, result);
        }
        for (int i = 0; i < WARMUP; i++)
            result.AddRange(At(scenario, i).Select(p => new Strike(START + i * STEP, p)));
        double start = START + WARMUP * STEP;
        double end = START + (COUNT - 1) * STEP;
        double duration = end - start - 4;
        // Integrate linear frequency exactly: phase(t) = (2t - .875t²/duration) / STEP.
        for (int i = 0; ; i++)
        {
            double phase = i * STEP;
            double rampPhase = 1.125 * duration;
            double elapsed = phase <= rampPhase
                ? (2 - Math.Sqrt(4 - 3.5 * phase / duration)) * duration / 1.75
                : duration + (phase - rampPhase) / .25;
            double time = start + elapsed;
            if (time > end) break;
            result.AddRange(At(scenario, WARMUP + (doubles ? i / 2 : i))
                .Select(p => new Strike(time, p)));
        }
        return AddIndependentFeet(scenario, result);
    }

    private static List<Strike> AddIndependentFeet(Scenario scenario, List<Strike> strikes)
    {
        if (scenario is Scenario.AllWithHat or Scenario.AllWithPedal)
        {
            for (double time = START; time <= START + (COUNT - 1) * STEP; time += .5)
            {
                strikes.Add(new Strike(time, Pad.Kick));
                if (scenario == Scenario.AllWithPedal) strikes.Add(new Strike(time + .25, Pad.HatPedal));
            }
        }
        return strikes.OrderBy(s => s.Time).ToList();
    }

    [Test]
    public void DoubleStrokesPreserveAggregateCadenceAndBonhamIsLinear()
    {
        foreach (var scenario in new[] { Scenario.AB, Scenario.ABAC, Scenario.BonhamABK })
        {
            var single = Schedule(scenario, false);
            var twice = Schedule(scenario, true);
            Assert.That(twice.Select(s => s.Time), Is.EqualTo(single.Select(s => s.Time)));
            var pattern = Pattern(scenario);
            Assert.That(twice.Skip(WARMUP).Take(pattern.Length * 2).Select(s => s.Pad),
                Is.EqualTo(Enumerable.Range(0, pattern.Length).Select(i => pattern[(WARMUP + i) % pattern.Length]).SelectMany(p => new[] { p, p })));
        }
        Assert.That(Schedule(Scenario.BonhamABK, false, true).GroupBy(s => s.Time)
            .All(g => g.Count() == 1), Is.True, "Bonham is A-B-kick, never a chord");
        var times = Schedule(Scenario.AB, false).Skip(WARMUP).Select(s => s.Time).ToArray();
        Assert.That(times[1] - times[0], Is.EqualTo(STEP / 2).Within(.001));
        Assert.That(times[^1] - times[^2], Is.EqualTo(STEP / .25).Within(1e-10));
    }

    [TestCase(Scenario.AllWithHat)]
    [TestCase(Scenario.AllWithPedal)]
    public void AllLaneHandsAreLinearAndFeetKeepIndependentTime(Scenario scenario)
    {
        bool Hand(Strike s) => s.Pad != Pad.Kick && s.Pad != Pad.HatPedal;
        var literal = Schedule(scenario, false, true);
        Assert.That(literal.Where(Hand).GroupBy(s => s.Time).All(g => g.Count() == 1), Is.True);
        Assert.That(literal.Where(Hand).Take(Pattern(scenario).Length).Select(s => s.Pad),
            Is.EqualTo(Pattern(scenario)));
        foreach (bool doubles in new[] { false, true })
        {
            var ramp = Schedule(scenario, doubles);
            Assert.That(ramp.Where(s => !Hand(s)), Is.EqualTo(literal.Where(s => !Hand(s))));
            Assert.That(ramp.Where(Hand).Select(s => s.Time),
                Is.EqualTo(Schedule(scenario, false).Where(Hand).Select(s => s.Time)));
        }
    }

    public static IEnumerable<TestCaseData> SweepCases()
    {
        foreach (var scenario in Enum.GetValues<Scenario>())
        foreach (double window in new[] { .160, .200 })
        foreach (bool doubles in new[] { false, true })
        foreach (var mode in Enum.GetValues<Playback>())
        {
            if (mode == Playback.ClassicPro && scenario != Scenario.AB) continue;
            yield return new TestCaseData(scenario, mode, window, doubles);
        }
    }

    [TestCaseSource(nameof(SweepCases))]
    public void CadenceSweepReportsNativeAndConvertedFailures(Scenario scenario, Playback mode,
        double window, bool doubles)
    {
        var strikes = Schedule(scenario, doubles);
        var result = Run(scenario, mode, window, strikes);
        Assert.That(Run(scenario, mode, window, strikes), Is.EqualTo(result), "fresh-run determinism");
        Assert.That(result.InLaneOverhits, Is.Zero, "Valid strikes inside entered lanes must never overhit.");
        TestContext.Out.WriteLine($"SWEEP {scenario},{mode},window={window},doubles={doubles}: " +
            $"break={result.FirstBreak:F6},miss={result.FirstMiss:F6},overhit={result.FirstOverhit:F6}," +
            $"rateAtBreak={Rate(result.FirstBreak):F3}x,hits={result.Hits},misses={result.Misses}," +
            $"overhits={result.Overhits},inLaneOverhits={result.InLaneOverhits},total={result.Total}");
        foreach (var (name, group) in new[] { ("hands", result.Hands), ("kick", result.Kicks), ("pedal", result.Pedals) })
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"MISS-GROUP {scenario},{mode},window={window},doubles={doubles},{name}: count={group.Count},first={group.FirstJudgment:F6},note={group.FirstNote:F6},pad={group.FirstPad},rate={Rate(group.FirstJudgment):F3},previousStrike={group.PreviousStrike:F6},nextStrike={group.NextStrike:F6}"));
    }

    [TestCase(Scenario.AB)]
    [TestCase(Scenario.ABAC)]
    [TestCase(Scenario.FourHandsAndKick)]
    [TestCase(Scenario.BonhamABK)]
    [TestCase(Scenario.AllWithHat)]
    [TestCase(Scenario.AllWithPedal)]
    public void ExactChartInputsProvideAPlayableControl(Scenario scenario)
    {
        foreach (var mode in Enum.GetValues<Playback>())
        {
            if (mode == Playback.ClassicPro && scenario != Scenario.AB) continue;
            var result = Run(scenario, mode, .2, Schedule(scenario, false, true));
            Assert.That(result.Misses, Is.Zero, $"{mode} exact authored input control");
            Assert.That(result.Overhits, Is.Zero, $"{mode} exact authored input control");
        }
    }

    [TestCase(.16, false)]
    [TestCase(.2, false)]
    [TestCase(.16, true)]
    [TestCase(.2, true)]
    public void TwoLaneCadenceMatchesClassicPro(double window, bool doubles)
    {
        var strikes = Schedule(Scenario.AB, doubles);
        var expected = Run(Scenario.AB, Playback.ClassicPro, window, strikes);
        foreach (var mode in new[] { Playback.NativeElite, Playback.ConvertedPro })
        {
            var actual = Run(Scenario.AB, mode, window, strikes);
            Assert.That((actual.Hits, actual.Misses, actual.FirstMiss, actual.Judgments),
                Is.EqualTo((expected.Hits, expected.Misses, expected.FirstMiss, expected.Judgments)), mode.ToString());
        }
    }

    [TestCase(.16, false)]
    [TestCase(.2, false)]
    [TestCase(.16, true)]
    [TestCase(.2, true)]
    public void ThreeLaneCadenceMatchesConvertedPro(double window, bool doubles)
    {
        var strikes = Schedule(Scenario.ABAC, doubles);
        var native = Run(Scenario.ABAC, Playback.NativeElite, window, strikes);
        var converted = Run(Scenario.ABAC, Playback.ConvertedPro, window, strikes);
        Assert.That((converted.Hits, converted.Misses, converted.Judgments),
            Is.EqualTo((native.Hits, native.Misses, native.Judgments)));
    }

    [TestCase(60)]
    [TestCase(120)]
    [TestCase(180)]
    public void MovingLaneGroupsAndTempoChangesRemainPlayable(int middleStepTicks)
    {
        var pads = new[] { Pad.Tom1, Pad.Tom2, Pad.Tom3 };
        var authored = new List<Strike>();
        void Phase(int firstSecond, int lastSecond, int step, params int[] pattern)
        {
            int index = 0;
            for (int tick = firstSecond * 960; tick < lastSecond * 960; tick += step)
                authored.Add(new Strike(tick / 960.0, pads[pattern[index++ % pattern.Length]]));
        }
        Phase(1, 2, 120, 0);
        Phase(2, 3, middleStepTicks, 0, 1);
        Phase(3, 5, 120, 0, 1, 0, 2);
        Phase(5, 6, middleStepTicks, 1, 2);
        Phase(6, 7, 120, 0, 2);
        Phase(7, 9, middleStepTicks, 1, 2);
        var lanes = new[] { (pads[0], 1.0, 5.0), (pads[0], 6.0, 7.0),
            (pads[1], 2.0, 6.0), (pads[1], 7.0, 9.0), (pads[2], 3.0, 9.0) };
        foreach (var mode in new[] { Playback.NativeElite, Playback.ConvertedPro })
        {
            var control = Run(Scenario.ABAC, mode, .2, authored, authored, lanes);
            Assert.That(control.Misses, Is.Zero, mode.ToString());
            Assert.That(control.Overhits, Is.Zero, mode.ToString());
            // Skip occasional interior notes, but preserve every authored lane entry.
            var entries = lanes.Select(l => authored.First(s => s.Pad == l.Item1 && s.Time >= l.Item2)).ToHashSet();
            var sparse = authored.Where((s, i) => i % 8 != 6 || entries.Contains(s)).ToList();
            var forgiven = Run(Scenario.ABAC, mode, .2, sparse, authored, lanes);
            Assert.That(forgiven.Hits, Is.GreaterThan(sparse.Count), "Sparse playing must exercise automatic coverage.");
            Assert.That(forgiven.InLaneOverhits, Is.Zero);
            // Remove C completely. AB input must never enter C's new phrase for free.
            var withoutC = authored.Where(s => s.Pad != pads[2]).ToList();
            var omitted = Run(Scenario.ABAC, mode, .2, withoutC, authored, lanes);
            Assert.That(omitted.Misses, Is.GreaterThanOrEqualTo(authored.Count(s => s.Pad == pads[2])));
            Assert.That(omitted.InLaneOverhits, Is.Zero);
        }
    }

    private static double? Rate(double? time) => time.HasValue
        ? Math.Clamp(2 - 1.75 * (time.Value - (START + WARMUP * STEP)) /
            ((COUNT - 1 - WARMUP) * STEP - 4), .25, 2) : null;

    [TestCase(.14, 7)]
    [TestCase(.3, 37)]
    [TestCase(.3, 83)]
    public void TwoLaneJudgmentsAreIndependentOfFrameInterval(double hitWindow, int frameMilliseconds)
    {
        var strikes = Schedule(Scenario.AB, false);
        foreach (var mode in new[] { Playback.ClassicPro, Playback.NativeElite, Playback.ConvertedPro })
        {
            var reference = Run(Scenario.AB, mode, .2, strikes, hitWindow: hitWindow, frameMilliseconds: 1);
            var actual = Run(Scenario.AB, mode, .2, strikes, hitWindow: hitWindow,
                frameMilliseconds: frameMilliseconds);
            Assert.That(actual.Judgments, Is.EqualTo(reference.Judgments), mode.ToString());
        }
    }

    [Test]
    public void AcceptedEarlyEntriesRefreshCadenceAcrossWideHitWindows()
    {
        var authored = Schedule(Scenario.AB, false, true).Take(6).ToList();
        var strikes = authored.Take(2).Select(s => new Strike(s.Time - .1, s.Pad)).ToList();
        foreach (var mode in new[] { Playback.ClassicPro, Playback.NativeElite, Playback.ConvertedPro })
        {
            var actual = Run(Scenario.AB, mode, .3, strikes, authored, hitWindow: .3);
            Assert.That(actual.Hits, Is.EqualTo(3),
                $"{mode}: two early entries must cover the third note, then silence must expire.");
        }
    }

    [Test]
    public void AbandonedHandMissesThenRecoversWithoutOverhits()
    {
        var authored = Schedule(Scenario.ABAC, false, true);
        var strikes = authored.Where(s => s.Pad != Pad.Tom1 || s.Time < 4 || s.Time >= 8).ToList();
        foreach (var mode in new[] { Playback.NativeElite, Playback.ConvertedPro })
        {
            var result = Run(Scenario.ABAC, mode, .2, strikes);
            Assert.That(result.Misses, Is.GreaterThan(0), mode.ToString());
            Assert.That(result.InLaneOverhits, Is.Zero, mode.ToString());
            Assert.That(result.Judgments.Split(',').Where(j =>
                double.Parse(j.Split(':')[0], System.Globalization.CultureInfo.InvariantCulture) >= 9)
                .All(j => j.EndsWith(":True")), Is.True, $"{mode}: returning A must recover.");
        }
    }

    [TestCase(Scenario.FourHandsAndKick, false)]
    [TestCase(Scenario.FourHandsAndKick, true)]
    [TestCase(Scenario.BonhamABK, false)]
    [TestCase(Scenario.BonhamABK, true)]
    public void ExactKickControlIsolatesHandCadence(Scenario scenario, bool doubles)
    {
        var authored = Schedule(scenario, false, true);
        var ramp = Schedule(scenario, doubles);
        // Preserve every hand timestamp; replace only feet with their charted timestamps.
        var exactFeet = ramp.Where(s => s.Pad != Pad.Kick)
            .Concat(authored.Where(s => s.Pad == Pad.Kick)).OrderBy(s => s.Time).ToList();
        foreach (var mode in new[] { Playback.NativeElite, Playback.ConvertedPro })
        {
            var original = Run(scenario, mode, .2, ramp);
            var control = Run(scenario, mode, .2, exactFeet);
            Assert.That(control.Kicks.Count, Is.Zero);
            Assert.That(control.Hands.Count, Is.EqualTo(original.Hands.Count));
            Assert.That(control.Hands.FirstNote, Is.EqualTo(original.Hands.FirstNote));
            Assert.That(control.InLaneOverhits, Is.Zero);
        }
    }

    private static Outcome Run(Scenario scenario, Playback mode, double window, List<Strike> strikes,
        List<Strike>? authoredOverride = null, (Pad Pad, double Start, double End)[]? laneOverrides = null,
        double hitWindow = .14, int frameMilliseconds = 10)
    {
        var song = new MoonSong(480); // Default 120 BPM.
        var moon = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
        var authored = authoredOverride ?? Schedule(scenario, false, true);
        foreach (var strike in authored)
            moon.Insert(new MoonNote((uint)Math.Round(strike.Time * 960), (int)strike.Pad));
        var lanes = laneOverrides ?? authored.Select(s => s.Pad).Distinct()
            .Select(p => (Pad: p, Start: START, End: START + COUNT * STEP)).ToArray();
        foreach (var lane in lanes)
            moon.Insert(new MoonPhrase((uint)Math.Round(lane.Start * 960),
                (uint)Math.Round((lane.End - lane.Start) * 960),
                Enum.Parse<MoonPhrase.Type>($"EliteDrums_{lane.Pad}Lane")));
        var settings = ParseSettings.Default;
        settings.DrumsType = DrumsType.FourLane;
        var loader = new MoonSongLoader(song, settings);
        var eliteTrack = loader.LoadEliteDrumsTrack(Instrument.EliteDrums);
        var elite = eliteTrack.GetDifficulty(Difficulty.Expert);
        var pro = loader.LoadEliteDrumsDownchartTrack(Instrument.ProDrums, eliteTrack)
            .GetDifficulty(Difficulty.Expert);
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var mapping = pro.Notes.SelectMany(n => n.ChildNotes.Prepend(n))
            .Where(n => n.ConversionOrigin != null)
            .GroupBy(n => (Pad)n.ConversionOrigin!.Source.Pad)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Pad).Distinct().Single());
        if (scenario is Scenario.AB or Scenario.ABAC)
            Assert.That(mapping.Values.Distinct().Count(), Is.EqualTo(Pattern(scenario).Distinct().Count()),
                "Parity fixtures must retain distinct output pads after conversion.");
        if (mode == Playback.ClassicPro)
        {
            var notes = authored.Select((s, i) => new DrumNote(
                i % 2 == 0 ? FourLaneDrumPad.YellowCymbal : FourLaneDrumPad.BlueCymbal,
                DrumNoteType.Neutral, DrumNoteFlags.None,
                NoteFlags.Trill | (i == 0 ? NoteFlags.LaneStart : 0) | (i == authored.Count - 1 ? NoteFlags.LaneEnd : 0),
                s.Time, (uint)Math.Round(s.Time * 960), false)).ToList();
            for (int i = 1; i < notes.Count; i++) { notes[i-1].NextNote = notes[i]; notes[i].PreviousNote = notes[i-1]; }
            pro = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert, notes, new(), new());
            mapping[Pad.RightCrash] = (int)FourLaneDrumPad.YellowCymbal;
            mapping[Pad.Ride] = (int)FourLaneDrumPad.BlueCymbal;
        }
        var parameters = new DrumsEngineParameters(new HitWindowSettings(hitWindow, hitWindow, 1, false, 0, 1, 1, window, .1),
            4, Thresholds, Thresholds, mode == Playback.NativeElite ? DrumsEngineParameters.DrumMode.Elite
                : DrumsEngineParameters.DrumMode.ProFourLane, false, true,
            DrumsEngineParameters.ELITE_FILL_RULESET_V1, 1f);
        double? firstBreak = null, firstMiss = null, firstOverhit = null;
        var overhitTimes = new List<double>();
        var missEvents = new List<(string Group, double Judgment, double Note, int Pad, string Name)>();
        BaseEngine engine;
        Func<int> hits, misses, overhits, total;
        if (mode == Playback.NativeElite)
        {
            var e = new EliteDrumsEngine(elite, sync, parameters, false, true);
            e.OnNoteMissed += (_, n) =>
            {
                firstMiss ??= e.CurrentTime;
                string group = n.Pad == (int)Pad.Kick ? "kick" : n.Pad == (int)Pad.HatPedal ? "pedal" : "hands";
                if (!missEvents.Any(m => m.Group == group)) TraceCadence(e, group, n.Pad, n.Time);
                missEvents.Add((group, e.CurrentTime, n.Time, n.Pad, ((Pad)n.Pad).ToString()));
            };
            e.OnOverhit += () => { firstOverhit ??= e.CurrentTime; overhitTimes.Add(e.CurrentTime); };
            engine = e; hits = () => e.EngineStats.NotesHit; misses = () => e.EngineStats.NotesMissed;
            overhits = () => e.EngineStats.Overhits; total = () => e.EngineStats.TotalNotes;
        }
        else
        {
            var e = new YargDrumsEngine(pro, sync, parameters, false, false);
            Assert.That(e.EliteFillV1Active, Is.EqualTo(mode == Playback.ConvertedPro));
            e.OnNoteMissed += (_, n) =>
            {
                firstMiss ??= e.CurrentTime;
                string group = n.Pad == (int)FourLaneDrumPad.Kick ? "kick" : "hands";
                if (!missEvents.Any(m => m.Group == group)) TraceCadence(e, group, n.Pad, n.Time);
                missEvents.Add((group, e.CurrentTime, n.Time, n.Pad, ((FourLaneDrumPad)n.Pad).ToString()));
            };
            e.OnOverhit += () => { firstOverhit ??= e.CurrentTime; overhitTimes.Add(e.CurrentTime); };
            engine = e; hits = () => e.EngineStats.NotesHit; misses = () => e.EngineStats.NotesMissed;
            overhits = () => e.EngineStats.Overhits; total = () => e.EngineStats.TotalNotes;
        }
        bool established = false;
        engine.OnComboIncrement += _ => established = true;
        engine.OnComboReset += () => { if (established) firstBreak ??= engine.CurrentTime; };
        engine.Update(0);
        // Step the engine independently of input density, so missing input cannot defer observation.
        var playable = strikes.Where(s => mode == Playback.NativeElite || mapping.ContainsKey(s.Pad))
            .DistinctBy(s => (s.Time, mode == Playback.NativeElite ? (int)s.Pad : mapping[s.Pad]));
        var inputs = playable
            .Select(s => mode == Playback.NativeElite ? GameInput.Create(s.Time, EliteAction(s.Pad), 1f)
                : GameInput.Create(s.Time, ProAction(mapping[s.Pad]), 1f)).ToList();
        foreach (var value in inputs) { var input = value; engine.QueueInput(ref input); }
        for (int ms = 1; ms <= (START + COUNT * STEP + 1) * 1000; ms += frameMilliseconds) { engine.Update(ms / 1000.0); }
        if (mode == Playback.NativeElite)
            Assert.That(elite.Notes.SelectMany(n => n.ChildNotes.Prepend(n))
                .All(n => n.WasHit || n.WasMissed || n.IsInvisibleTerminator), Is.True);
        else
            Assert.That(pro.Notes.SelectMany(n => n.ChildNotes.Prepend(n))
                .All(n => n.WasHit || n.WasMissed), Is.True);
        TestContext.Out.WriteLine($"FIXTURE authored={authored.Count},scored={total()}," +
            $"mappedPads={mapping.Count},sourcePads={authored.Select(s => s.Pad).Distinct().Count()}");
        Assert.That(hits(), Is.GreaterThan(0), "warmup must establish gameplay");
        int inLane = overhitTimes.Count(t => strikes.Any(s => Math.Abs(s.Time - t) < 1e-9 &&
            authored.Any(a => a.Pad == s.Pad && a.Time <= t) &&
            authored.Any(a => a.Pad == s.Pad && a.Time >= t)));
        string judgments = mode == Playback.NativeElite
            ? string.Join(",", elite.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Where(n => !n.IsInvisibleTerminator)
                .Select(n => FormattableString.Invariant($"{n.Time:R}:{n.WasHit}")))
            : string.Join(",", pro.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Select(n => FormattableString.Invariant($"{n.Time:R}:{n.WasHit}")));
        MissGroup Group(string name)
        {
            var events = missEvents.Where(e => e.Group == name).ToArray();
            if (events.Length == 0) return new MissGroup(0, null, null, null, null, null);
            var first = events[0];
            var padStrikes = playable.Where(s => (mode == Playback.NativeElite ? (int)s.Pad : mapping[s.Pad]) == first.Pad)
                .Select(s => (double?)s.Time).ToArray();
            return new MissGroup(events.Length, first.Judgment, first.Note, first.Name,
                padStrikes.LastOrDefault(t => t <= first.Note), padStrikes.FirstOrDefault(t => t > first.Note));
        }
        Assert.That(missEvents.Count, Is.EqualTo(misses()), "Every missed physical note must be attributed once.");
        return new Outcome(firstBreak, firstMiss, firstOverhit, hits(), misses(), overhits(), total(), inLane, judgments,
            Group("hands"), Group("kick"), Group("pedal"));
    }

    // Optional read-only state inspection for investigating a first miss; production code is untouched.
    private static void TraceCadence(BaseEngine engine, string group, int pad, double noteTime)
    {
        if (Environment.GetEnvironmentVariable("YARG_CADENCE_DIAGNOSTICS") != "1") return;
        static object? Field(object value, string name)
        {
            for (var type = value.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (field != null) return field.GetValue(value);
            }
            throw new InvalidOperationException($"Missing diagnostic field {name}.");
        }
        object? owner = Field(engine, engine is EliteDrumsEngine ? "_authoredLanes" : "_eliteFillRuntime");
        if (owner is null) return;
        var cadence = Field(owner, "_handCadence")!;
        var participation = (Dictionary<int, double>)Field(cadence, "_participation")!;
        var events = ((ValueTuple<int, double>[])Field(cadence, "_events")!);
        double lastInput = (double)Field(cadence, "_lastTime")!;
        var nearby = events.Where(n => Math.Abs(n.Item2 - lastInput) <= 1)
            .OrderBy(n => Math.Abs(n.Item2 - lastInput)).Take(32).ToArray();
        TestContext.Out.WriteLine(FormattableString.Invariant(
            $"CADENCE-STATE {engine.GetType().Name} group={group} pad={pad} note={noteTime:R} sharedExpiry={Field(cadence, "_expiry")} padExpiry={participation.GetValueOrDefault(pad, double.NegativeInfinity):R} lastInput={lastInput:R} lastPad={Field(cadence, "_lastPad")} localCounts={string.Join("/", nearby.GroupBy(n => n.Item1).Select(g => $"{g.Key}:{g.Count()}"))}"));
        if (owner is EliteFillRuntimeScheduler runtime)
            TestContext.Out.WriteLine($"CADENCE-MEMBERSHIP pad={pad} lanes={runtime.AuthoredLanes.Count(l => l.Members.Any(n => n.Pad == pad))}");
    }

    private static EliteDrumsAction EliteAction(Pad pad) => pad switch
    {
        Pad.Kick => EliteDrumsAction.Kick, Pad.HatPedal => EliteDrumsAction.EliteStomp,
        Pad.HiHat => EliteDrumsAction.EliteOpenHiHat,
        _ => Enum.Parse<EliteDrumsAction>($"Elite{pad}")
    };
    private static DrumsAction ProAction(int pad) => Enum.Parse<DrumsAction>(((FourLaneDrumPad)pad).ToString());
}
