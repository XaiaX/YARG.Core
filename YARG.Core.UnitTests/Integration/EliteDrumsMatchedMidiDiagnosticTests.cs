using System;
using System.Linq;
using System.Collections.Generic;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Integration;

/// <summary>Opt-in diagnostic for a locally supplied matched PART DRUMS / PART ELITE_DRUMS MIDI.</summary>
public sealed class EliteDrumsMatchedMidiDiagnosticTests
{
    [Test]
    public void ExpertCrashLane_ReportsNativeAndConvertedPads()
    {
        var path = Environment.GetEnvironmentVariable("YARG_MATCHED_ELITE_MIDI");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set YARG_MATCHED_ELITE_MIDI to the matched comparison MIDI.");

        var settings = ParseSettings.Default_Midi;
        settings.EliteDrumsDownchartOutputs = new[] { Instrument.ProDrums };
        var chart = SongChart.FromMidi(in settings, MidiFile.Read(path!));
        var native = chart.ProDrums.GetDifficulty(Difficulty.Expert);
        var converted = chart.EliteDrumsDowncharts[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);

        const uint start = 134080;
        const uint end = 136230;
        var nativeNotes = PhysicalNotes(native.Notes)
            .Where(note => note.Tick >= start && note.Tick < end && note.Pad != (int) FourLaneDrumPad.Kick)
            .Select(note => (note.Tick, note.Pad)).OrderBy(note => note.Tick).ThenBy(note => note.Pad).ToArray();
        var convertedNotes = PhysicalNotes(converted.Notes)
            .Where(note => note.Tick >= start && note.Tick < end && note.Pad != (int) FourLaneDrumPad.Kick)
            .Select(note => (note.Tick, note.Pad)).OrderBy(note => note.Tick).ThenBy(note => note.Pad).ToArray();

        TestContext.Progress.WriteLine($"Native in-lane: {nativeNotes.Length}; converted in-lane: {convertedNotes.Length}");
        foreach (var tick in nativeNotes.Select(note => note.Tick).Union(convertedNotes.Select(note => note.Tick)).OrderBy(tick => tick))
        {
            var left = string.Join(",", nativeNotes.Where(note => note.Tick == tick).Select(note => note.Pad));
            var right = string.Join(",", convertedNotes.Where(note => note.Tick == tick).Select(note => note.Pad));
            if (left != right) TestContext.Progress.WriteLine($"tick {tick}: native [{left}] converted [{right}]");
        }

        Assert.That(nativeNotes, Is.Not.Empty);
        Assert.That(convertedNotes, Is.Not.Empty);
        Assert.That(convertedNotes, Is.EqualTo(nativeNotes), "matched in-lane physical ticks/pads must agree before identical-input replay");

        // Start immediately before the common phrase. Clone because engines mutate note outcomes.
        var nativeReplay = native.Clone();
        var eliteReplay = converted.Clone();
        var nativeEngine = CreateEngine(nativeReplay, chart.SyncTrack);
        var eliteEngine = CreateEngine(eliteReplay, chart.SyncTrack);
        var inputNotes = PhysicalNotes(native.Notes)
            .Where(note => note.Tick >= start && note.Tick < end && note.Pad != (int) FourLaneDrumPad.Kick)
            .OrderBy(note => note.Time).ThenBy(note => note.Pad).ToArray();
        Assert.That(inputNotes.All(note => note.Pad is (int) FourLaneDrumPad.BlueCymbal or (int) FourLaneDrumPad.GreenCymbal),
            Is.True, "this replay is restricted to the matched blue/green crash lanes");
        foreach (var note in inputNotes)
        {
            var action = note.Pad == (int) FourLaneDrumPad.BlueCymbal
                ? DrumsAction.BlueCymbal : DrumsAction.GreenCymbal;
            var first = GameInput.Create(note.Time, action, 1f);
            var second = GameInput.Create(note.Time, action, 1f);
            nativeEngine.QueueInput(ref first);
            eliteEngine.QueueInput(ref second);
            nativeEngine.Update(note.Time);
            eliteEngine.Update(note.Time);
        }
        var after = inputNotes[^1].Time + 0.5;
        nativeEngine.Update(after);
        eliteEngine.Update(after);
        TestContext.Progress.WriteLine($"Replay hits: native {nativeEngine.EngineStats.NotesHit}; Elite {eliteEngine.EngineStats.NotesHit}");
        Assert.That(nativeEngine.EngineStats.NotesHit, Is.EqualTo(inputNotes.Length));
        Assert.That(eliteEngine.EngineStats.NotesHit, Is.EqualTo(nativeEngine.EngineStats.NotesHit));
        var nativeOutcomes = PhysicalNotes(nativeReplay.Notes)
            .Where(note => note.Tick >= start && note.Tick < end && note.Pad != (int) FourLaneDrumPad.Kick)
            .Select(note => (note.Tick, note.Pad, note.WasHit)).OrderBy(note => note.Tick).ThenBy(note => note.Pad).ToArray();
        var eliteOutcomes = PhysicalNotes(eliteReplay.Notes)
            .Where(note => note.Tick >= start && note.Tick < end && note.Pad != (int) FourLaneDrumPad.Kick)
            .Select(note => (note.Tick, note.Pad, note.WasHit)).OrderBy(note => note.Tick).ThenBy(note => note.Pad).ToArray();
        Assert.That(nativeOutcomes.All(note => note.WasHit), Is.True);
        Assert.That(eliteOutcomes, Is.EqualTo(nativeOutcomes), "each matched physical note must have the same outcome");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExpertCrashLane_OpeningInputsThenSilence(bool precedingOverhit)
    {
        var path = Environment.GetEnvironmentVariable("YARG_MATCHED_ELITE_MIDI");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set YARG_MATCHED_ELITE_MIDI to the matched comparison MIDI.");
        var settings = ParseSettings.Default_Midi;
        settings.EliteDrumsDownchartOutputs = new[] { Instrument.ProDrums };
        var chart = SongChart.FromMidi(in settings, MidiFile.Read(path!));
        var native = chart.ProDrums.GetDifficulty(Difficulty.Expert).Clone();
        var elite = chart.EliteDrumsDowncharts[Instrument.ProDrums].GetDifficulty(Difficulty.Expert).Clone();
        var nativeEngine = CreateEngine(native, chart.SyncTrack);
        var eliteEngine = CreateEngine(elite, chart.SyncTrack);
        const uint start = 134080;
        const uint end = 136230;
        var openings = PhysicalNotes(native.Notes).Where(note => note.Tick is 134080 or 134120)
            .OrderBy(note => note.Time).ToArray();
        Assert.That(openings.Select(note => note.Pad), Is.EquivalentTo(new[]
        {
            (int) FourLaneDrumPad.GreenCymbal, (int) FourLaneDrumPad.BlueCymbal
        }));
        if (precedingOverhit)
        {
            // The preceding phrase has finished; make the barrier explicit before the new entry.
            var timestamp = openings[0].Time - 0.08;
            foreach (var engine in new[] { nativeEngine, eliteEngine })
            {
                var extra = GameInput.Create(timestamp, DrumsAction.RedDrum, 1f);
                engine.QueueInput(ref extra);
                engine.Update(timestamp);
            }
        }
        foreach (var note in openings)
        {
            var action = note.Pad == (int) FourLaneDrumPad.GreenCymbal
                ? DrumsAction.GreenCymbal : DrumsAction.BlueCymbal;
            foreach (var engine in new[] { nativeEngine, eliteEngine })
            {
                var input = GameInput.Create(note.Time, action, 1f);
                engine.QueueInput(ref input);
                engine.Update(note.Time);
            }
        }
        var finalTime = PhysicalNotes(native.Notes).First(note => note.Tick >= end).Time + 0.3;
        nativeEngine.Update(finalTime);
        eliteEngine.Update(finalTime);
        var nativeOutcomes = PhysicalNotes(native.Notes).Where(note => note.Tick >= start && note.Tick < end && note.Pad != (int) FourLaneDrumPad.Kick)
            .Select(note => (note.Tick, note.Pad, note.WasHit)).OrderBy(note => note.Tick).ThenBy(note => note.Pad).ToArray();
        var eliteOutcomes = PhysicalNotes(elite.Notes).Where(note => note.Tick >= start && note.Tick < end && note.Pad != (int) FourLaneDrumPad.Kick)
            .Select(note => (note.Tick, note.Pad, note.WasHit)).OrderBy(note => note.Tick).ThenBy(note => note.Pad).ToArray();
        var differences = nativeOutcomes.Zip(eliteOutcomes).Where(pair => pair.First != pair.Second).Take(12).ToArray();
        TestContext.Progress.WriteLine($"overhit={precedingOverhit}: native hits={nativeOutcomes.Count(note => note.WasHit)}, elite hits={eliteOutcomes.Count(note => note.WasHit)}; differences={differences.Length}");
        foreach (var pair in differences) TestContext.Progress.WriteLine($"native {pair.First}; elite {pair.Second}");
        Assert.That(nativeOutcomes.Length, Is.EqualTo(53));
        Assert.That(eliteOutcomes.Length, Is.EqualTo(nativeOutcomes.Length));
        Assert.That(nativeOutcomes.Where(note => note.WasHit).Select(note => note.Tick),
            Is.EqualTo(new uint[] { 134080, 134120, 134160, 134200 }),
            "after the two opening inputs, native forgiveness ends before later lane notes");
        Assert.That(eliteOutcomes, Is.EqualTo(nativeOutcomes),
            "authored lanes must forgive the same opening continuations and miss the same later notes");
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
        {
            foreach (var physical in note.AllNotes) yield return physical;
        }
    }
}
