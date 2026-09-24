using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Integration;

/// <summary>
/// Opt-in diagnostic replay for dense Expert/Expert+ kicks in a supplied Elite Drums MIDI.
/// Set YARG_DOUBLE_KICK_ELITE_MIDI to the local MIDI path to run.
/// </summary>
public sealed class EliteDrumsDoubleKickLaneDiagnosticTests
{
    private const uint INTERVAL_START_TICK = 126880;
    private const uint INTERVAL_END_TICK = 128880;

    [Test]
    public void ExpertAndExpertPlus_ReplaySamePhysicalKickSequence()
    {
        var path = Environment.GetEnvironmentVariable("YARG_DOUBLE_KICK_ELITE_MIDI");
        if (string.IsNullOrWhiteSpace(path))
            Assert.Ignore("Set YARG_DOUBLE_KICK_ELITE_MIDI to the local Elite Drums regression MIDI.");

        var settings = ParseSettings.Default_Midi;
        settings.EliteDrumsDownchartOutputs = new[] { Instrument.ProDrums };
        var chart = SongChart.FromMidi(in settings, MidiFile.Read(path!));
        Assert.That(chart.EliteDrumsDowncharts, Does.ContainKey(Instrument.ProDrums),
            "The MIDI must contain an Elite Drums track that downcharts to ProDrums.");

        var downchart = chart.EliteDrumsDowncharts[Instrument.ProDrums];
        var expert = downchart.GetDifficulty(Difficulty.Expert).Clone();
        var expertPlus = downchart.GetDifficulty(Difficulty.ExpertPlus).Clone();
        var allKickEvents = PhysicalNotes(expertPlus.Notes)
            .Where(note => note.Pad == (int) FourLaneDrumPad.Kick)
            .OrderBy(note => note.Time).ThenBy(note => note.Tick)
            .Select(note => new InputEvent(note.Time, DrumsAction.Kick, note.Tick, note.IsDoubleKick))
            .ToArray();
        var focusKickEvents = allKickEvents
            .Where(input => input.Tick >= INTERVAL_START_TICK && input.Tick < INTERVAL_END_TICK)
            .ToArray();

        TestContext.Progress.WriteLine($"MIDI={path}");
        TestContext.Progress.WriteLine($"Interval ticks [{INTERVAL_START_TICK},{INTERVAL_END_TICK}); " +
            $"Expert notes={PhysicalNotes(expert.Notes).Count()}, Expert+ notes={PhysicalNotes(expertPlus.Notes).Count()}");
        TestContext.Progress.WriteLine($"Expert+ all-chart kick events={allKickEvents.Length}; " +
            $"interval events={focusKickEvents.Length}; " +
            $"interval 1x={focusKickEvents.Count(input => !input.IsDoubleKick)}, 2x={focusKickEvents.Count(input => input.IsDoubleKick)}");
        foreach (var kick in focusKickEvents)
            TestContext.Progress.WriteLine($"kick tick={kick.Tick} time={kick.Time:F6} marking={(kick.IsDoubleKick ? "2x" : "1x")}");

        Assert.That(focusKickEvents, Is.Not.Empty,
            "No Expert+ downcharted kicks were found in the requested dense interval.");
        Assert.That(focusKickEvents.Any(input => input.IsDoubleKick), Is.True,
            "No 2x-marked kick exists in the requested interval; verify MIDI selection/range.");
        Assert.That(focusKickEvents.Any(input => !input.IsDoubleKick), Is.True,
            "No 1x-marked kick exists in the requested interval; verify MIDI selection/range.");
        var expertLaneKicks = PhysicalNotes(expert.Notes)
            .Where(note => note.Pad == (int) FourLaneDrumPad.Kick &&
                note.Tick >= INTERVAL_START_TICK && note.Tick < INTERVAL_END_TICK).ToArray();
        TestContext.Progress.WriteLine($"Expert kick lane notes={expertLaneKicks.Count(note => note.IsKickLane)}; " +
            $"starts={string.Join(",", expertLaneKicks.Where(note => note.IsKickLaneStart).Select(note => note.Tick))}; " +
            $"ends={string.Join(",", expertLaneKicks.Where(note => note.IsKickLaneEnd).Select(note => note.Tick))}");
        Assert.That(expertLaneKicks.Length, Is.EqualTo(focusKickEvents.Count(input => !input.IsDoubleKick)));
        Assert.That(expertLaneKicks.All(note => note.IsKickLane), Is.True,
            "the extra 2x inputs must be assessed against a surviving Expert kick lane");
        Assert.That(expertLaneKicks.Any(note => note.IsKickLaneStart), Is.True);

        var expertEngine = CreateEngine(expert, chart.SyncTrack);
        var expertPlusEngine = CreateEngine(expertPlus, chart.SyncTrack);
        Replay(expert, expertEngine, allKickEvents);
        Replay(expertPlus, expertPlusEngine, allKickEvents);

        var expertKickOutcomes = OutcomesForTicks(expert, focusKickEvents);
        var expertPlusKickOutcomes = OutcomesForTicks(expertPlus, focusKickEvents);
        TestContext.Progress.WriteLine($"SUMMARY Expert notesHit={expertEngine.EngineStats.NotesHit}/" +
            $"{expertEngine.EngineStats.TotalNotes} overhits={expertEngine.EngineStats.Overhits} " +
            $"combo={expertEngine.BaseStats.Combo} maxCombo={expertEngine.BaseStats.MaxCombo}");
        TestContext.Progress.WriteLine($"SUMMARY Expert+ notesHit={expertPlusEngine.EngineStats.NotesHit}/" +
            $"{expertPlusEngine.EngineStats.TotalNotes} overhits={expertPlusEngine.EngineStats.Overhits} " +
            $"combo={expertPlusEngine.BaseStats.Combo} maxCombo={expertPlusEngine.BaseStats.MaxCombo}");

        foreach (var input in focusKickEvents)
        {
            var expertAtTick = expertKickOutcomes.Where(note => note.Tick == input.Tick).ToArray();
            var expertPlusAtTick = expertPlusKickOutcomes.Where(note => note.Tick == input.Tick).ToArray();
            TestContext.Progress.WriteLine($"OUTCOME tick={input.Tick} marking={(input.IsDoubleKick ? "2x" : "1x")} " +
                $"Expert=[{FormatOutcomes(expertAtTick)}] Expert+=[{FormatOutcomes(expertPlusAtTick)}]");
        }

        Assert.That(expertKickOutcomes.Length, Is.EqualTo(focusKickEvents.Length),
            "Report every physical kick tick against the Expert track, including ticks where that track has no kick.");
        Assert.That(expertPlusKickOutcomes.Length, Is.EqualTo(focusKickEvents.Length),
            "Report every physical kick tick against the Expert+ track.");
        Assert.That(expertKickOutcomes.Where(outcome => focusKickEvents.Single(input => input.Tick == outcome.Tick).IsDoubleKick)
            .All(outcome => outcome.ChartNoteCount == 0), Is.True,
            "Expert should not chart the 2x-marked Expert+ physical kicks in this regression section.");
        Assert.That(expertKickOutcomes.Where(outcome => !focusKickEvents.Single(input => input.Tick == outcome.Tick).IsDoubleKick)
            .All(outcome => outcome.ChartNoteCount == 1 && outcome.Hits == 1 && outcome.Misses == 0), Is.True,
            "Every shared 1x kick must hit once in Expert.");
        Assert.That(expertPlusKickOutcomes.All(outcome => outcome.ChartNoteCount == 1 && outcome.Hits == 1 && outcome.Misses == 0),
            Is.True, "Every physical 1x and 2x kick must hit once in Expert+.");
        Assert.That(expertEngine.EngineStats.Overhits, Is.EqualTo(0),
            "uncharted 2x kick strikes within the surviving Expert kick lane should be protected");
        Assert.That(expertPlusEngine.EngineStats.Overhits, Is.EqualTo(0),
            "the Expert+ chart should consume the complete shared kick sequence without overhits");
        Assert.That(expertEngine.BaseStats.Combo, Is.EqualTo(expertEngine.EngineStats.NotesHit),
            "the extra pedal strikes must not break Expert's otherwise perfect combo");
    }

    private static void Replay(InstrumentDifficulty<DrumNote> chartNotes, YargDrumsEngine engine,
        IReadOnlyList<InputEvent> sharedKickEvents)
    {
        // Reproduce the full song, not merely the printed interval, so combo and timeout behavior are meaningful.
        // Hand inputs follow each mode's own chart; kick inputs are shared exactly from Expert+.
        var events = new List<InputEvent>();
        events.AddRange(sharedKickEvents);
        foreach (var note in PhysicalNotes(chartNotes.Notes).Where(note => note.Pad != (int) FourLaneDrumPad.Kick))
        {
            if (TryGetAction(note.Pad, out var action))
                events.Add(new InputEvent(note.Time, action, note.Tick, false));
        }

        foreach (var inputEvent in events.OrderBy(input => input.Time).ThenBy(input => input.Tick)
                     .ThenBy(input => input.Action))
        {
            var input = GameInput.Create(inputEvent.Time, inputEvent.Action, 1f);
            engine.QueueInput(ref input);
            engine.Update(inputEvent.Time);
        }

        var endTime = Math.Max(chartNotes.GetEndTime(), events.Count == 0 ? 0 : events.Max(input => input.Time)) + 1.0;
        engine.Update(endTime);
    }

    private static KickOutcome[] OutcomesForTicks(InstrumentDifficulty<DrumNote> difficulty,
        IReadOnlyList<InputEvent> kickEvents)
    {
        var notes = PhysicalNotes(difficulty.Notes)
            .Where(note => note.Pad == (int) FourLaneDrumPad.Kick)
            .ToArray();
        return kickEvents.Select(input =>
        {
            var matching = notes.Where(note => note.Tick == input.Tick).ToArray();
            return new KickOutcome(input.Tick, matching.Length,
                matching.Count(note => note.WasHit), matching.Count(note => note.WasMissed));
        }).ToArray();
    }

    private static string FormatOutcomes(IEnumerable<KickOutcome> outcomes) => string.Join(",",
        outcomes.Select(outcome => outcome.ChartNoteCount == 0 ? "no-chart-kick" :
            $"chart={outcome.ChartNoteCount}/hit={outcome.Hits}/miss={outcome.Misses}"));

    private static bool TryGetAction(int pad, out DrumsAction action)
    {
        action = (FourLaneDrumPad) pad switch
        {
            FourLaneDrumPad.RedDrum => DrumsAction.RedDrum,
            FourLaneDrumPad.YellowDrum => DrumsAction.YellowDrum,
            FourLaneDrumPad.YellowCymbal => DrumsAction.YellowCymbal,
            FourLaneDrumPad.BlueDrum => DrumsAction.BlueDrum,
            FourLaneDrumPad.BlueCymbal => DrumsAction.BlueCymbal,
            FourLaneDrumPad.GreenDrum => DrumsAction.GreenDrum,
            FourLaneDrumPad.GreenCymbal => DrumsAction.GreenCymbal,
            _ => default,
        };
        return (FourLaneDrumPad) pad is FourLaneDrumPad.RedDrum or FourLaneDrumPad.YellowDrum or
            FourLaneDrumPad.YellowCymbal or FourLaneDrumPad.BlueDrum or FourLaneDrumPad.BlueCymbal or
            FourLaneDrumPad.GreenDrum or FourLaneDrumPad.GreenCymbal;
    }

    private static YargDrumsEngine CreateEngine(InstrumentDifficulty<DrumNote> difficulty, SyncTrack sync)
    {
        var window = new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, 0.16, 0.25);
        var parameters = new DrumsEngineParameters(window, 4,
            new[] { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f },
            new[] { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f },
            DrumsEngineParameters.DrumMode.ProFourLane, false, true,
            DrumsEngineParameters.ELITE_FILL_RULESET_V1, 1f);
        return new YargDrumsEngine(difficulty, sync, parameters, isBot: false, isMidiDrumsInput: false);
    }

    private static IEnumerable<DrumNote> PhysicalNotes(IEnumerable<DrumNote> notes)
    {
        foreach (var note in notes)
            foreach (var physical in note.AllNotes)
                yield return physical;
    }

    private readonly record struct InputEvent(double Time, DrumsAction Action, uint Tick, bool IsDoubleKick);
    private readonly record struct KickOutcome(uint Tick, int ChartNoteCount, int Hits, int Misses);
}
