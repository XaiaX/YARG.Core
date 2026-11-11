// pattern: Imperative Shell
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;
using static YARG.Core.Input.EliteDrumsAction;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteHiHatArticulationTests
{
    private static EliteDrumsEngine Create(EliteDrumNote target, bool enabled = true,
        byte sustainRuleset = 0, bool closure = false, EliteDrumNote? next = null)
    {
        var notes = new[]
        {
            EliteHiHatSustainTests.Note(.5, pad: EliteDrumPad.Snare), target, next,
            EliteHiHatSustainTests.Note(5, pad: EliteDrumPad.Snare)
        }.OfType<EliteDrumNote>().ToArray();
        for (int i = 1; i < notes.Length; i++)
        {
            notes[i - 1].NextNote = notes[i];
            notes[i].PreviousNote = notes[i - 1];
        }
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            notes.ToList(), new(), new());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var window = new HitWindowSettings(.2, .2, 1, false, 0, 1, 1, .1, .1);
        float[] thresholds = { 1, 2, 3, 4, 5, 6 };
        var parameters = new DrumsEngineParameters(window, 4, thresholds, thresholds,
            DrumsEngineParameters.DrumMode.Elite, false, true, hiHatClosureSuppression: closure,
            eliteHiHatSustains: sustainRuleset, lenientHiHatArticulation: enabled);
        var engine = new EliteDrumsEngine(chart, sync, parameters, false, true);
        Queue(engine, .5, EliteSnare);
        engine.Update(.6);
        return engine;
    }

    private static void Queue(EliteDrumsEngine engine, double time, EliteDrumsAction action, float velocity = .8f)
    {
        var input = GameInput.Create(time, action, velocity);
        engine.QueueInput(ref input);
    }

    [TestCase(EliteDrumsHatState.Closed, EliteSizzleHiHat, 25, 1)]
    [TestCase(EliteDrumsHatState.Open, EliteSizzleHiHat, 25, 1)]
    [TestCase(EliteDrumsHatState.Closed, EliteOpenHiHat, 12, 1)]
    [TestCase(EliteDrumsHatState.Open, EliteClosedHiHat, 12, 1)]
    [TestCase(EliteDrumsHatState.Open, EliteOpenHiHat, 50, 0)]
    [TestCase(EliteDrumsHatState.Indifferent, EliteSizzleHiHat, 50, 0)]
    [TestCase(EliteDrumsHatState.Indifferent, EliteClosedHiHat, 50, 0)]
    [TestCase(EliteDrumsHatState.Indifferent, EliteOpenHiHat, 50, 0)]
    public void InWindowHiHatScoresByArticulation(EliteDrumsHatState state, EliteDrumsAction action,
        int points, int misarticulations)
    {
        var note = EliteHiHatSustainTests.Note(1, state: state);
        var engine = Create(note);
        Queue(engine, 1, action);
        engine.Update(1);

        Assert.Multiple(() =>
        {
            Assert.That(note.WasHit, Is.True);
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(50 + points));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(2 - misarticulations));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.MisarticulatedHiHats, Is.EqualTo(misarticulations));
        });
    }

    [TestCase(EliteSizzleHiHat, true, 2, 0)]
    [TestCase(EliteClosedHiHat, false, 0, 1)]
    public void DisabledFeaturePreservesExistingHatJudgment(EliteDrumsAction action, bool hit, int combo, int overhits)
    {
        var note = EliteHiHatSustainTests.Note(1);
        var engine = Create(note, enabled: false);
        Queue(engine, 1, action);
        engine.Update(1);
        Assert.That(note.WasHit, Is.EqualTo(hit));
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(combo));
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(overhits));
        Assert.That(engine.EngineStats.MisarticulatedHiHats, Is.Zero);
    }

    [Test]
    public void OverlappingWindowsPreferTheExistingCorrectMatch()
    {
        var partial = EliteHiHatSustainTests.Note(1, state: EliteDrumsHatState.Closed);
        var exact = EliteHiHatSustainTests.Note(1.08, state: EliteDrumsHatState.Open);
        var engine = Create(partial, next: exact);
        Queue(engine, 1, EliteOpenHiHat);
        engine.Update(1);
        Assert.That(exact.WasHit, Is.True);
        Assert.That(partial.WasHit, Is.False);
        Assert.That(engine.EngineStats.MisarticulatedHiHats, Is.Zero);
    }

    [TestCase(DrumNoteType.Accent, .9f)]
    [TestCase(DrumNoteType.Ghost, .2f)]
    public void CorrectVelocityRemainsIndependentOfWrongArticulation(DrumNoteType dynamics, float velocity)
    {
        var note = new EliteDrumNote(EliteDrumPad.HiHat, dynamics, EliteDrumsHatState.Closed,
            EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumsChannelFlag.None, 1, 960, false);
        var engine = Create(note);
        Queue(engine, 1, EliteSizzleHiHat, velocity);
        engine.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(87));
            Assert.That(engine.EngineStats.DynamicsBonus, Is.EqualTo(12));
            Assert.That(engine.EngineStats.AccentsHit, Is.EqualTo(dynamics == DrumNoteType.Accent ? 1 : 0));
            Assert.That(engine.EngineStats.GhostsHit, Is.EqualTo(dynamics == DrumNoteType.Ghost ? 1 : 0));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
            Assert.That(engine.EngineStats.MisarticulatedHiHats, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CorrectPedalInChordStillEarnsItsOwnComboPoint(bool pedalFirst)
    {
        var note = EliteHiHatSustainTests.Note(1, state: EliteDrumsHatState.Closed);
        var pedal = EliteHiHatSustainTests.Note(1, pad: EliteDrumPad.HatPedal);
        note.AddChildNote(pedal);
        var engine = Create(note);
        Queue(engine, 1, pedalFirst ? EliteStomp : EliteOpenHiHat);
        Queue(engine, 1, pedalFirst ? EliteOpenHiHat : EliteStomp);
        engine.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(note.WasHit && pedal.WasHit, Is.True);
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(112));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.MisarticulatedHiHats, Is.EqualTo(1));
        });
    }

    [Test]
    public void WrongFootGestureCannotHitChartedPedal()
    {
        var note = EliteHiHatSustainTests.Note(1);
        var pedal = EliteHiHatSustainTests.Note(1, pad: EliteDrumPad.HatPedal,
            pedal: EliteDrumsHatPedalType.Splash);
        note.AddChildNote(pedal);
        var engine = Create(note);
        Queue(engine, 1, EliteClosedHiHat);
        Queue(engine, 1, EliteStomp);
        engine.Update(1);
        Assert.That(pedal.WasHit, Is.False);
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
        engine.Update(1.15);
        Assert.That(pedal.WasMissed, Is.True);
        Assert.That(engine.EngineStats.Combo, Is.Zero);
        Assert.That(engine.EngineStats.MisarticulatedHiHats, Is.EqualTo(1));
    }

    [TestCase(EliteClosedHiHat, 6, 0)]
    [TestCase(EliteSizzleHiHat, 12, 13)]
    public void OpenSustainHeadDiscountAndTailDependOnPhysicalArticulation(EliteDrumsAction action,
        int head, int tail)
    {
        var note = EliteHiHatSustainTests.Note(1, 1920);
        var engine = Create(note, sustainRuleset: 2);
        bool judged = false, judgedClosedAsOpen = false;
        engine.OnNoteHit += (_, _) =>
        {
            judged = engine.IsJudgingMisarticulatedHiHat;
            judgedClosedAsOpen = engine.IsJudgingClosedAsOpen;
        };
        Queue(engine, 1, action);
        engine.Update(1);
        Assert.That(judged, Is.True);
        Assert.That(judgedClosedAsOpen, Is.EqualTo(tail == 0));
        Assert.That(engine.IsJudgingMisarticulatedHiHat, Is.False);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(50 + head));
        var sustain = engine.HiHatSustains.Single();
        Assert.That(engine.WasHiHatSustainBroken(sustain), Is.EqualTo(tail == 0));
        engine.Update(sustain.EndTime + .05);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(50 + head + tail));
        Assert.That(engine.EngineStats.SustainScore, Is.EqualTo(tail));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CorrectSplashCanStillStartTheSharedTail(bool pedalFirst)
    {
        var note = EliteHiHatSustainTests.Note(1, 1920);
        note.AddChildNote(EliteHiHatSustainTests.Note(1, 1920, EliteDrumPad.HatPedal,
            pedal: EliteDrumsHatPedalType.Splash));
        var engine = Create(note, sustainRuleset: 2);
        Queue(engine, 1, pedalFirst ? EliteSplash : EliteClosedHiHat);
        Queue(engine, 1, pedalFirst ? EliteClosedHiHat : EliteSplash);
        engine.Update(1);
        var sustain = engine.HiHatSustains.Single();
        Assert.That(engine.ActiveHiHatSustain, Is.SameAs(sustain));
        Assert.That(engine.WasHiHatSustainBroken(sustain), Is.False);
        engine.Update(sustain.EndTime + .05);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(106));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PairedClosureDoesNotOverridePartialCredit(bool pedalFirst)
    {
        var note = EliteHiHatSustainTests.Note(1);
        var engine = Create(note, sustainRuleset: 2, closure: true);
        Queue(engine, 1, pedalFirst ? EliteStomp : EliteClosedHiHat);
        Queue(engine, 1.01, pedalFirst ? EliteClosedHiHat : EliteStomp);
        engine.Update(1.06);
        Assert.That(note.WasHit, Is.True);
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        Assert.That(engine.EngineStats.MisarticulatedHiHats, Is.EqualTo(1));
    }

    [Test]
    public void FollowUpFlamUsesItsOwnArticulationWithoutAnotherComboPoint()
    {
        var note = new EliteDrumNote(EliteDrumPad.HiHat, DrumNoteType.Neutral, EliteDrumsHatState.Closed,
            EliteDrumsHatPedalType.Stomp, true, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumsChannelFlag.None, 1, 960, false);
        var engine = Create(note);
        bool judged = false;
        engine.OnFlamFollowUp += (_, _, _, _) => judged = engine.IsJudgingMisarticulatedHiHat;
        Queue(engine, 1, EliteClosedHiHat);
        Queue(engine, 1.02, EliteSizzleHiHat);
        engine.Update(1.03);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(125));
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        Assert.That(engine.EngineStats.MisarticulatedHiHats, Is.EqualTo(1));
        Assert.That(judged, Is.True);
        Assert.That(engine.IsJudgingMisarticulatedHiHat, Is.False);
    }
}
