using System;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Replays;

[TestFixture]
public sealed class EliteFlamReplayContractTests
{
    [TestCase(0.01)]
    [TestCase(0.17)]
    [TestCase(4.0)]
    public void EliteFlamBotReplayRoundTripWithAdjacentRealNotesAndFinalFlam(double cadence)
    {
        var chart = NativeEliteReplayFixture.EliteChart();
        var track = chart.EliteDrums.GetDifficulty(Difficulty.Expert);
        track.Notes.Clear();
        var flat = NativeEliteReplayFixture.EliteNote(EliteDrumNote.EliteDrumPad.Snare, 1);
        flat.IsFlatFlam = true;
        var adjacent = NativeEliteReplayFixture.EliteNote(EliteDrumNote.EliteDrumPad.Snare, 1.01);
        var regular = NativeEliteReplayFixture.EliteNote(EliteDrumNote.EliteDrumPad.Tom1, 3);
        regular.IsFlam = true;
        track.Notes.AddRange(new[] { flat, adjacent, regular });
        DrumPlaybackPreparer.Rebuild(track);
        var parameters = NativeEliteReplayFixture.Parameters();
        parameters.SongSpeed = 1;
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.IsBot = true;
        var engine = new EliteDrumsEngine(track.Clone(), chart.SyncTrack, parameters, true, true);
        int lifecycle = 0, followUps = 0;
        engine.OnNoteHit += (_, _) => lifecycle++;
        engine.OnFlamFollowUp += (_, _, _, _) => followUps++;
        engine.Update(-2);
        for (double time = 0; time < 3.5; time += cadence) engine.Update(time);
        engine.Update(3.5);
        var live = new DrumsStats(engine.EngineStats);
        Assert.Multiple(() =>
        {
            Assert.That(live.NoteScore, Is.EqualTo(250));
            Assert.That(live.CommittedScore, Is.EqualTo(250));
            Assert.That(live.Combo, Is.EqualTo(3));
            Assert.That(live.NotesHit, Is.EqualTo(3));
            Assert.That(live.DynamicsBonus, Is.Zero);
            Assert.That(lifecycle, Is.EqualTo(3));
            Assert.That(followUps, Is.Zero, "Bot completion must not invent physical strikes.");
        });
        var data = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters,
            live, Array.Empty<GameInput>()));
        Assert.That(data.Frames[0].Inputs, Is.Empty);
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(chart, data));
    }

    [Test]
    public void EliteOldReplayRescoringLoadsWithoutRewritingStoredStatsOrInputs()
    {
        var chart = NativeEliteReplayFixture.EliteChart();
        var track = chart.EliteDrums.GetDifficulty(Difficulty.Expert);
        track.Notes[0].IsFlatFlam = true;
        DrumPlaybackPreparer.Rebuild(track);
        var parameters = NativeEliteReplayFixture.Parameters();
        parameters.SongSpeed = 1;
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        var inputs = NativeEliteReplayFixture.EliteInputs();
        var engine = new EliteDrumsEngine(track.Clone(), chart.SyncTrack, parameters, false, true);
        var rescored = NativeEliteReplayFixture.PlayLive(engine, inputs);
        var stored = new DrumsStats(rescored);
        stored.NoteScore -= 50;
        stored.CommittedScore -= 50;
        var data = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters, stored, inputs));
        var result = NativeEliteReplayFixture.Analyze(chart, data);
        Assert.That(((DrumsStats) result.ResultStats).NoteScore, Is.EqualTo(rescored.NoteScore));
        Assert.That(result.OriginalStats.CommittedScore, Is.EqualTo(stored.CommittedScore));
        Assert.That(data.Frames[0].Inputs, Has.Length.EqualTo(inputs.Length));
        Assert.That(data.Frames[0].Inputs[0].Time, Is.EqualTo(inputs[0].Time));
        Assert.That(result.ResultStats.CommittedScore, Is.EqualTo(stored.CommittedScore + 50));
    }
}
