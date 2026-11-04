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

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteHiHatSustainScoreTests
{
    internal static DrumsEngineParameters Parameters(bool legacy = false, bool closure = false)
    {
        var window = new HitWindowSettings(.2, .2, 1, false, 0, 1, 1, .1, .1);
        float[] thresholds = { 1, 2, 3, 4, 5, 6 };
        return legacy ? new(window, 4, thresholds, thresholds,
            DrumsEngineParameters.DrumMode.Elite, false, true, hiHatClosureSuppression: closure, eliteHiHatSustains: 1) :
            new(window, 4, thresholds, thresholds, DrumsEngineParameters.DrumMode.Elite, false, true, hiHatClosureSuppression: closure);
    }

    private static EliteDrumsEngine Create(EliteDrumNote[] notes, bool bot = false, bool closure = false)
    {
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            notes.ToList(), new(), new());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        return new EliteDrumsEngine(chart, sync, Parameters(closure: closure), bot, true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PairedPedalClosureStillStopsRewardWhenTheCymbalHitsAnIndifferentNote(bool pedalFirst)
    {
        var engine = Create(new[] { EliteHiHatSustainTests.Note(1, 1920),
            EliteHiHatSustainTests.Note(1.4, state: EliteDrumsHatState.Indifferent),
            EliteHiHatSustainTests.Note(5, pad: EliteDrumPad.Snare) }, closure: true);
        EliteHiHatSustainTests.Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        engine.Update(1.04);
        foreach (var action in pedalFirst ? new[] { EliteDrumsAction.EliteStomp, EliteDrumsAction.EliteClosedHiHat }
            : new[] { EliteDrumsAction.EliteClosedHiHat, EliteDrumsAction.EliteStomp })
        {
            var input = GameInput.Create(1.4, action, .8f);
            engine.QueueInput(ref input);
        }
        engine.Update(1.44);
        int stopped = engine.EngineStats.TotalScore;
        engine.Update(3.1);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(stopped));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        Assert.That(engine.ActiveHiHatSustain, Is.Null);
    }

    [Test]
    public void ClosingJustAfterPedalRollStopsRewardDespiteOrdinaryLaneProtection()
    {
        var engine = EliteHiHatSustainTests.PedalRollEngine(false, current: true);
        EliteHiHatSustainTests.Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        EliteHiHatSustainTests.Press(engine, 1.2, EliteDrumsAction.EliteSplash);
        EliteHiHatSustainTests.Press(engine, 2.5, EliteDrumsAction.EliteSplash);
        EliteHiHatSustainTests.Press(engine, 2.55, EliteDrumsAction.EliteStomp);
        int stopped = engine.EngineStats.TotalScore;
        Assert.That(engine.ActiveHiHatSustain, Is.Null);
        engine.Update(4);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(stopped));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [Test]
    public void ReservedPedalReleaseUsesSustainStateAfterInterveningGenerator()
    {
        var engine = Create(new[] { EliteHiHatSustainTests.Note(1, state: EliteDrumsHatState.Indifferent),
            EliteHiHatSustainTests.Note(1.1, 1920), EliteHiHatSustainTests.Note(5, pad: EliteDrumPad.Snare) }, closure: true);
        foreach (var inputValue in new[] { GameInput.Create(1, EliteDrumsAction.EliteClosedHiHat, .8f),
            GameInput.Create(1.01, EliteDrumsAction.EliteOpenHiHat, .8f),
            GameInput.Create(1.02, EliteDrumsAction.EliteStomp, .8f) })
        {
            var input = inputValue;
            engine.QueueInput(ref input);
        }
        engine.Update(1.06);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(engine.ActiveHiHatSustain, Is.Null);
        engine.Update(3.2);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(75));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [TestCase(240u)]
    [TestCase(1920u)]
    public void ShortAndLongSustainsSplitOneGemScoreEqually(uint length)
    {
        var engine = Create(new[] { EliteHiHatSustainTests.Note(1, length) });
        EliteHiHatSustainTests.Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(25), "Half on the initial hit.");
        double end = engine.HiHatSustains.Single().EndTime;
        engine.Update((1 + end) / 2);
        Assert.That(engine.EngineStats.TotalScore, Is.InRange(37, 38));
        engine.Update(end + .01);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(50), "Duration never changes the budget.");
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(25));
        Assert.That(engine.EngineStats.SustainScore, Is.EqualTo(25));
    }

    [TestCase(EliteDrumsAction.EliteStomp)]
    [TestCase(EliteDrumsAction.EliteSplash)]
    public void EarlyClosureOnlyForfeitsUnearnedScore(EliteDrumsAction action)
    {
        var engine = Create(new[] { EliteHiHatSustainTests.Note(1, 1920) });
        EliteHiHatSustainTests.Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        EliteHiHatSustainTests.Press(engine, 2, action);
        int stopped = engine.EngineStats.TotalScore;
        engine.Update(4);
        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.InRange(37, 38));
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(stopped));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
            Assert.That(engine.EngineStats.IsFullCombo, Is.True);
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.HiHatSustainBreaks, Is.Zero);
            Assert.That(engine.ActiveHiHatSustain, Is.Null);
        });
    }

    [Test]
    public void ClosingInsideEndWindowCompletesTheBudget()
    {
        var engine = Create(new[] { EliteHiHatSustainTests.Note(1, 1920) });
        EliteHiHatSustainTests.Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        EliteHiHatSustainTests.Press(engine, 2.91, EliteDrumsAction.EliteStomp);
        engine.Update(3.1);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(50));
        Assert.That(engine.ActiveHiHatSustain, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EachHitInOpenSplashChordHasItsOwnBudget(bool splashFirst)
    {
        var open = EliteHiHatSustainTests.Note(1, 1920);
        open.AddChildNote(EliteHiHatSustainTests.Note(1, 1920, EliteDrumPad.HatPedal,
            pedal: EliteDrumsHatPedalType.Splash));
        var engine = Create(new[] { open });
        foreach (var action in splashFirst
            ? new[] { EliteDrumsAction.EliteSplash, EliteDrumsAction.EliteOpenHiHat }
            : new[] { EliteDrumsAction.EliteOpenHiHat, EliteDrumsAction.EliteSplash })
            EliteHiHatSustainTests.Press(engine, 1, action);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(50));
        engine.Update(3.1);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(100));
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
    }

    [Test]
    public void NewEarlyHitCompletesPreviousTailAndResetClearsRewardState()
    {
        var engine = Create(new[] { EliteHiHatSustainTests.Note(1), EliteHiHatSustainTests.Note(1.5, 1920) });
        EliteHiHatSustainTests.Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        EliteHiHatSustainTests.Press(engine, 1.45, EliteDrumsAction.EliteOpenHiHat);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(75));
        engine.Update(3.6);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(100));
        engine.Reset();
        engine.Update(4);
        Assert.That(engine.EngineStats.TotalScore, Is.Zero);
    }

    [Test]
    public void LateHitCanStillEarnItsWholeBudgetAndRepeatedUpdatesCannotDuplicateIt()
    {
        var engine = Create(new[] { EliteHiHatSustainTests.Note(1, 1920) });
        EliteHiHatSustainTests.Press(engine, 1.09, EliteDrumsAction.EliteOpenHiHat);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(25));
        engine.Update(3.1); engine.Update(3.1); engine.Update(4);
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(50));
    }

    [Test]
    public void TailRetainsTheStartingGemMultiplierAcrossLaterHitsAndFrameCadences()
    {
        int Run(bool dense)
        {
            var notes = Enumerable.Range(0, 9).Select(i => EliteHiHatSustainTests.Note(.1 * i,
                pad: EliteDrumPad.Snare)).Append(EliteHiHatSustainTests.Note(1, 1920))
                .Append(EliteHiHatSustainTests.Note(1.5, pad: EliteDrumPad.Snare)).ToArray();
            var engine = Create(notes);
            foreach (var note in notes)
            {
                if (dense && note.Time > 1) for (double t = 1.01; t < note.Time; t += .01) engine.Update(t);
                EliteHiHatSustainTests.Press(engine, note.Time, note.Pad == (int)EliteDrumPad.Snare
                    ? EliteDrumsAction.EliteSnare : EliteDrumsAction.EliteOpenHiHat);
            }
            if (dense) for (double t = 1.51; t < 3; t += .01) engine.Update(t);
            engine.Update(3.1);
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(650)); // 9*50 + 50*2 + 50*2
            return engine.EngineStats.TotalScore;
        }
        Assert.That(Run(false), Is.EqualTo(Run(true)));
    }
}
