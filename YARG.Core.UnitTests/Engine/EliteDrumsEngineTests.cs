using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

public class EliteDrumsEngineTests
{
    private static EliteDrumNote MakeNote(EliteDrumNote.EliteDrumPad pad, double time = 1,
        EliteDrumNote.EliteDrumsHatState hat = EliteDrumNote.EliteDrumsHatState.Indifferent,
        EliteDrumNote.EliteDrumsHatPedalType pedal = EliteDrumNote.EliteDrumsHatPedalType.Stomp,
        DrumNoteType dynamics = DrumNoteType.Neutral) =>
        new(pad, dynamics, hat, pedal, false, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumNote.EliteDrumsChannelFlag.None, time, (uint) (time * 960), false);

    private static EliteDrumsEngine Create(params EliteDrumNote[] notes)
    {
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new(notes), new(), new());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.ProFourLane);
        return new EliteDrumsEngine(chart, sync, parameters, false, true);
    }

    private static void Press(EliteDrumsEngine engine, double time, EliteDrumsAction action, float velocity = 0.8f)
    {
        var input = GameInput.Create(time, action, velocity);
        engine.QueueInput(ref input);
        engine.Update(time);
    }

    [TestCase(EliteDrumNote.EliteDrumPad.Kick, EliteDrumsAction.Kick)]
    [TestCase(EliteDrumNote.EliteDrumPad.Snare, EliteDrumsAction.EliteSnare)]
    [TestCase(EliteDrumNote.EliteDrumPad.LeftCrash, EliteDrumsAction.EliteLeftCrash)]
    [TestCase(EliteDrumNote.EliteDrumPad.Tom1, EliteDrumsAction.EliteTom1)]
    [TestCase(EliteDrumNote.EliteDrumPad.Tom2, EliteDrumsAction.EliteTom2)]
    [TestCase(EliteDrumNote.EliteDrumPad.Tom3, EliteDrumsAction.EliteTom3)]
    [TestCase(EliteDrumNote.EliteDrumPad.Ride, EliteDrumsAction.EliteRide)]
    [TestCase(EliteDrumNote.EliteDrumPad.RightCrash, EliteDrumsAction.EliteRightCrash)]
    [TestCase(EliteDrumNote.EliteDrumPad.HatPedal, EliteDrumsAction.EliteStomp)]
    public void MatchingActionScoresRealNote(EliteDrumNote.EliteDrumPad pad, EliteDrumsAction action)
    {
        var note = MakeNote(pad);
        var engine = Create(note);
        int events = 0;
        engine.OnPadHit += (_, hit, _, _, _, _) => { if (hit) events++; };
        Press(engine, 1, action);
        Assert.Multiple(() =>
        {
            Assert.That(note.WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.NoteScore, Is.GreaterThan(0));
            Assert.That(engine.EngineStats.CommittedScore, Is.GreaterThan(0));
            Assert.That(events, Is.EqualTo(1));
        });
    }

    [TestCase(EliteDrumNote.EliteDrumsHatState.Open, EliteDrumsAction.EliteOpenHiHat, true)]
    [TestCase(EliteDrumNote.EliteDrumsHatState.Open, EliteDrumsAction.EliteSizzleHiHat, true)]
    [TestCase(EliteDrumNote.EliteDrumsHatState.Open, EliteDrumsAction.EliteClosedHiHat, false)]
    [TestCase(EliteDrumNote.EliteDrumsHatState.Closed, EliteDrumsAction.EliteClosedHiHat, true)]
    [TestCase(EliteDrumNote.EliteDrumsHatState.Closed, EliteDrumsAction.EliteSizzleHiHat, true)]
    [TestCase(EliteDrumNote.EliteDrumsHatState.Closed, EliteDrumsAction.EliteOpenHiHat, false)]
    [TestCase(EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumsAction.EliteOpenHiHat, true)]
    [TestCase(EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumsAction.EliteClosedHiHat, true)]
    [TestCase(EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumsAction.EliteSizzleHiHat, true)]
    public void HiHatActionMatrix(EliteDrumNote.EliteDrumsHatState hat, EliteDrumsAction action, bool shouldHit)
    {
        var note = MakeNote(EliteDrumNote.EliteDrumPad.HiHat, hat: hat);
        var engine = Create(note);
        Press(engine, 1, action);
        Assert.Multiple(() =>
        {
            Assert.That(note.WasHit, Is.EqualTo(shouldHit));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(shouldHit ? 1 : 0));
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(shouldHit ? 50 : 0));
        });
    }

    [TestCase(EliteDrumsAction.EliteStomp)]
    [TestCase(EliteDrumsAction.EliteSplash)]
    public void UnmatchedPedalDoesNotBreakComboOrScore(EliteDrumsAction pedal)
    {
        var first = MakeNote(EliteDrumNote.EliteDrumPad.Snare, 1);
        var second = MakeNote(EliteDrumNote.EliteDrumPad.Snare, 3);
        first.NextNote = second;
        second.PreviousNote = first;
        var engine = Create(first, second);
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        int score = engine.EngineStats.CommittedScore;
        int unmatched = 0;
        engine.OnPadHit += (action, hit, _, _, _, _) =>
        {
            if (action == pedal && !hit) unmatched++;
        };
        Press(engine, 1.5, pedal);
        Press(engine, 2, pedal);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(score));
            Assert.That(unmatched, Is.EqualTo(2));
        });
        Press(engine, 3, EliteDrumsAction.EliteSnare);
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
    }

    [TestCase(EliteDrumsAction.EliteSnare)]
    [TestCase(EliteDrumsAction.EliteStomp)]
    public void PostSongInputStillEmitsHarmlessPadFeedback(EliteDrumsAction action)
    {
        var note = MakeNote(EliteDrumNote.EliteDrumPad.Snare);
        var engine = Create(note);
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        engine.Update(2);
        int feedback = 0;
        engine.OnPadHit += (received, hit, _, _, _, _) =>
        {
            if (received == action && !hit) feedback++;
        };
        Press(engine, 3, action);
        Assert.Multiple(() =>
        {
            Assert.That(feedback, Is.EqualTo(1));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
        });
    }

    [Test]
    public void ChartedPedalStillRequiresASeparateStrike()
    {
        var first = MakeNote(EliteDrumNote.EliteDrumPad.Snare, 1);
        var pedal = MakeNote(EliteDrumNote.EliteDrumPad.HatPedal, 2);
        var last = MakeNote(EliteDrumNote.EliteDrumPad.Snare, 3);
        first.NextNote = pedal;
        pedal.PreviousNote = first;
        pedal.NextNote = last;
        last.PreviousNote = pedal;
        var engine = Create(first, pedal, last);
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Press(engine, 1.5, EliteDrumsAction.EliteStomp); // too early; not held for the gem
        engine.Update(2.5);
        Assert.Multiple(() =>
        {
            Assert.That(pedal.WasMissed, Is.True);
            Assert.That(pedal.WasHit, Is.False);
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.Combo, Is.Zero);
        });
        Press(engine, 3, EliteDrumsAction.EliteSnare);
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
    }

    [Test]
    public void SplashIsNotStomp_AndMismatchAfterFirstNoteDoesNotOverhit()
    {
        var first = MakeNote(EliteDrumNote.EliteDrumPad.Snare);
        var splash = MakeNote(EliteDrumNote.EliteDrumPad.HatPedal, 2,
            pedal: EliteDrumNote.EliteDrumsHatPedalType.Splash);
        first.NextNote = splash;
        splash.PreviousNote = first;
        var engine = Create(first, splash);
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Press(engine, 2, EliteDrumsAction.EliteStomp);
        Assert.Multiple(() =>
        {
            Assert.That(splash.WasHit, Is.False);
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.OverhitsByAction.GetValueOrDefault((int) EliteDrumsAction.EliteStomp), Is.Zero);
        });
        Press(engine, 2.01, EliteDrumsAction.EliteSplash);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
    }

    [Test]
    public void InvisibleTerminatorNeverScoresOrReceivesAnInputHit()
    {
        var first = MakeNote(EliteDrumNote.EliteDrumPad.Snare);
        var terminator = MakeNote(EliteDrumNote.EliteDrumPad.HatPedal, 2,
            pedal: EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator);
        first.NextNote = terminator;
        terminator.PreviousNote = first;
        var engine = Create(first, terminator);
        Assert.That(engine.EngineStats.TotalNotes, Is.EqualTo(1));
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        int score = engine.EngineStats.CommittedScore;
        Press(engine, 2, EliteDrumsAction.EliteStomp);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(50));
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(score));
            Assert.That(engine.BaseScore, Is.EqualTo(50));
        });
    }

    [Test]
    public void BotScoresChordThenSkipsTerminatorWithoutIncreasingCombo()
    {
        var snare = MakeNote(EliteDrumNote.EliteDrumPad.Snare);
        var kick = MakeNote(EliteDrumNote.EliteDrumPad.Kick);
        snare.AddChildNote(kick);
        var terminator = MakeNote(EliteDrumNote.EliteDrumPad.HatPedal, 2,
            pedal: EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator);
        snare.NextNote = terminator;
        terminator.PreviousNote = snare;
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new() { snare, terminator }, new(), new());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.ProFourLane);
        var engine = new EliteDrumsEngine(chart, sync, parameters, true, true);
        engine.Update(2.5);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.TotalNotes, Is.EqualTo(2));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(100));
            Assert.That(engine.BaseScore, Is.EqualTo(100));
            Assert.That(terminator.WasHit, Is.True);
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void InvisibleChordMemberNeverConsumesStrikeRegardlessOfOrder(bool controlFirst)
    {
        var control = MakeNote(EliteDrumNote.EliteDrumPad.HatPedal, 1,
            pedal: EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator);
        var snare = MakeNote(EliteDrumNote.EliteDrumPad.Snare);
        var parent = controlFirst ? control : snare;
        parent.AddChildNote(controlFirst ? snare : control);
        var engine = Create(parent);
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Assert.That(control.WasHit, Is.True);
        Assert.That(snare.WasHit, Is.True);
        Assert.That(engine.EngineStats.TotalNotes, Is.EqualTo(1));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
    }

    [Test]
    public void AccentedHitAwardsDynamicsBonus_AndReleaseDoesNotScore()
    {
        var note = MakeNote(EliteDrumNote.EliteDrumPad.Snare, dynamics: DrumNoteType.Accent);
        var engine = Create(note);
        var release = GameInput.Create(0.9, EliteDrumsAction.EliteSnare, 0f);
        engine.QueueInput(ref release);
        engine.Update(0.9);
        Assert.That(engine.EngineStats.NotesHit, Is.Zero);
        Press(engine, 1, EliteDrumsAction.EliteSnare, 1f);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.DynamicsBonus, Is.EqualTo(25));
            Assert.That(engine.EngineStats.AccentsHit, Is.EqualTo(1));
        });
    }
}
