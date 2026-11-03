// pattern: Imperative Shell
using System;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Replays;
using YARG.Core.Replays.Analyzer;
using YARG.Core.UnitTests.Engine;
using YARG.Core.UnitTests.Replays;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Integration;

public sealed class EliteCymbalMergeMidiDiagnosticTests
{
    private static System.Collections.Generic.IEnumerable<TestCaseData> MergeCases()
    {
        foreach (string choice in EliteCymbalPrimaryTests.Choices)
            yield return new TestCaseData(choice, EliteDrumsCymbalProjection.CURRENT_POLICY);
        foreach (string id in new[] { "left-crash+ride", "left-crash+right-crash", "ride+right-crash" })
            yield return new TestCaseData(id, EliteDrumsCymbalProjection.UNORDERED_POLICY);
    }

    [TestCaseSource(nameof(MergeCases))]
    public void FlagpoleMidiPreservesSourcesAndInterleavedRollsThroughEveryMerge(string pair, byte policy)
    {
        var path = Environment.GetEnvironmentVariable("YARG_ELITE_CYMBAL_MIDI");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("Set YARG_ELITE_CYMBAL_MIDI to the Flagpole Sitta Elite MIDI fixture.");
        var settings = ParseSettings.Default_Midi;
        // This Elite-only update has no native drum format for the full-chart fallback
        // loader to infer. Supply the ordinary four-lane fallback configuration.
        settings.DrumsType = DrumsType.FourLane;
        var chart = SongChart.FromMidi(in settings, MidiFile.Read(path!));
        var original = chart.EliteDrums.GetDifficulty(Difficulty.Expert);
        var originals = original.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
        var sourceSnapshot = originals.Select(n => (n.Pad, n.Tick, n.Time, n.Dynamics,
            n.IsAuthoredFlam, n.IsDoubleKick, n.SourceDefinition)).ToArray();
        var notes = original.Clone();
        var layout = policy == EliteDrumsCymbalProjection.UNORDERED_POLICY ? EliteCymbalMergeTests.Layout(pair) :
            EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 8, pair);
        EliteDrumsCymbalProjection.Apply(notes, layout, policy);
        var playable = notes.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
        Assert.That(playable.Length, Is.EqualTo(originals.Length));
        Assert.That(playable.SelectMany(n => n.SourceDefinitions),
            Is.EquivalentTo(originals.SelectMany(n => n.SourceDefinitions)), "Every immutable authored identity survives projection.");
        Assert.That(playable.Select(n => (n.OriginalPad, n.Tick, n.Time, n.Dynamics, n.IsAuthoredFlam, n.IsDoubleKick, n.SourceDefinition)),
            Is.EqualTo(sourceSnapshot));
        var overlap = playable.Where(n => n.Tick >= 167040 && n.Tick <= 172240 &&
            n.OriginalPad is (int)EliteDrumPad.LeftCrash or (int)EliteDrumPad.Ride or (int)EliteDrumPad.RightCrash).ToArray();
        Assert.That(overlap.GroupBy(n => n.OriginalPad).Select(g => (g.Key, g.Count())).OrderBy(g => g.Key),
            Is.EqualTo(new[] { ((int)EliteDrumPad.LeftCrash, 33), ((int)EliteDrumPad.Ride, 17), ((int)EliteDrumPad.RightCrash, 16) }.OrderBy(g => g.Item1)));
        Assert.That(overlap.Any(n => n.IsCymbalExpanded), Is.False, "Overlapping lane intervals never manufacture simultaneous onsets.");
        Assert.That(overlap.Select(n => n.Pad).Distinct().Count(), Is.EqualTo(2));
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.IsBot = true;
        profile.AutoHiHatPedal = false;
        profile.ReplayEliteDrumsLayout = layout;
        var template = NativeEliteReplayFixture.Parameters();
        var parameters = new DrumsEngineParameters(template.HitWindow, template.MaxMultiplier,
            template.StarMultiplierThresholds, template.SoloBonusStarMultiplierThresholds, template.Mode,
            template.NoStarPowerOverlap, template.EnableLanes, eliteCymbalMergePolicy: policy);
        parameters.SongSpeed = 1;
        var rawSegments = EliteDrumsPlayableLaneSegments.Resolve(notes);
        var physicalSegments = policy == EliteDrumsCymbalProjection.CURRENT_POLICY ?
            EliteDrumsPlayableLaneSegments.ResolvePhysical(notes) : rawSegments;
        Assert.That(physicalSegments.Count, Is.LessThanOrEqualTo(rawSegments.Count));
        var physicalByPad = string.Join(",", physicalSegments.GroupBy(segment => segment.Pad)
            .OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}"));
        // Match initialized gameplay and replay preparation, including recorded activators.
        notes.SetDrumActivationFlags(profile.StarPowerActivationType);
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, true, true, cymbalLayout: layout);
        foreach (var note in notes.Notes) engine.Update(note.Time);
        double end = chart.GetEndTime() + 1;
        engine.Update(end);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(engine.EngineStats.TotalNotes));
        var data = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters,
            engine.EngineStats, Array.Empty<YARG.Core.Input.GameInput>()));
        var info = new ReplayInfo("", "", ReplayIO.REPLAY_VERSIONS.CURRENT, 0, default, "", "", "", default,
            DateTime.UtcNow, 1, end, 0, default, Array.Empty<PauseInfo>(), false, Array.Empty<ReplayStats>());
        var result = ReplayAnalyzer.AnalyzeReplay(chart, info, data).Single();
        Assert.That(result.Passed, Is.True, result.StatLog);
        Assert.That(original.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Select(n => (n.Pad, n.Tick, n.Time,
            n.Dynamics, n.IsAuthoredFlam, n.IsDoubleKick, n.SourceDefinition)), Is.EqualTo(sourceSnapshot));
        TestContext.Progress.WriteLine($"{pair},policy={policy}: source={originals.Length}, playable={playable.Length}, aliases={playable.Sum(n => n.SourceDefinitions.Count)}, overlap={overlap.Length}, projectedPads=2, expansion=0, rawStrips={rawSegments.Count}, physicalStrips={physicalSegments.Count}, stripsByPad={physicalByPad}, botHits={engine.EngineStats.NotesHit}, replay=pass");
    }
}
