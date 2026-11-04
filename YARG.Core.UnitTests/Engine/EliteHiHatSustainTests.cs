// pattern: Imperative Shell
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteHiHatSustainTests
{
    internal static EliteDrumNote Note(double time, uint length = 0,
        EliteDrumPad pad = EliteDrumPad.HiHat,
        EliteDrumsHatState state = EliteDrumsHatState.Open,
        EliteDrumsHatPedalType pedal = EliteDrumsHatPedalType.Stomp)
    {
        uint tick = (uint)System.Math.Round(time * 960);
        return new EliteDrumNote(pad, DrumNoteType.Neutral, state, pedal, false,
            DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, tick, false,
            new EliteDrumSourceDefinition($"sustain:{pad}:{tick}", (int)tick, (int)pad,
                tick, length, time, length / 960d));
    }

    internal static EliteDrumsEngine Create(EliteDrumNote[] notes, bool assisted = false, bool bot = false,
        bool closure = false)
    {
        var ordered = notes.OrderBy(note => note.Time).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].PreviousNote = i > 0 ? ordered[i - 1] : null;
            ordered[i].NextNote = i + 1 < ordered.Count ? ordered[i + 1] : null;
        }
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            ordered, new(), new());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var window = new YARG.Core.Engine.HitWindowSettings(.2, .2, 1, false, 0, 1, 1, .1, .1);
        float[] thresholds = { 1, 2, 3, 4, 5, 6 };
        var parameters = new DrumsEngineParameters(window, 4, thresholds, thresholds,
            DrumsEngineParameters.DrumMode.Elite, false, true, hiHatClosureSuppression: closure,
            eliteHiHatSustains: 1);
        return new EliteDrumsEngine(chart, sync, parameters, bot, true, assisted);
    }

    internal static void Press(EliteDrumsEngine engine, double time, EliteDrumsAction action)
    {
        var input = GameInput.Create(time, action, .8f);
        engine.QueueInput(ref input);
        engine.Update(time);
    }

    [TestCase(EliteDrumsAction.EliteStomp)]
    [TestCase(EliteDrumsAction.EliteSplash)]
    public void EarlyClosureIsOnePedalOverhitWithoutDeductingScore(EliteDrumsAction action)
    {
        var engine = Create(new[] { Note(1), Note(3, pad: EliteDrumPad.Snare) });
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        int earned = engine.EngineStats.TotalScore;
        int unhitBefore = engine.EngineStats.NotesMissed;
        Press(engine, 1.2, action);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.Combo, Is.Zero);
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
            Assert.That(engine.EngineStats.OverhitsByAction.GetValueOrDefault((int)action), Is.EqualTo(1));
            Assert.That(engine.EngineStats.NotesMissed, Is.EqualTo(unhitBefore));
            Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(earned));
        });
        Press(engine, 1.3, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1), "A broken sustain cannot penalize twice.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClosureAfterFinalGemStillInvalidatesFullCombo(bool assisted)
    {
        var engine = Create(new[] { Note(1, 1920) }, assisted);
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Assert.That(engine.EngineStats.IsFullCombo, Is.True);
        Press(engine, 2, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.IsFullCombo, Is.False);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Assert.That(engine.EngineStats.NotesMissed, Is.Zero);
    }

    [TestCase(0u, 1.64, 1)]
    [TestCase(0u, 1.65, 0)]
    [TestCase(1920u, 2.89, 1)]
    [TestCase(1920u, 2.9, 0)]
    [TestCase(1920u, 3.1, 0)]
    public void GeneratedAndManualEndsUseInclusiveOrdinaryEarlyWindow(uint length, double close, int overhits)
    {
        var engine = Create(new[] { Note(1, length), Note(5, pad: EliteDrumPad.Snare) });
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, close, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(overhits));
    }

    [Test]
    public void IndifferentHitDoesNotTerminateAnOpenSustain()
    {
        var engine = Create(new[] { Note(1, 1920), Note(1.4, state: EliteDrumsHatState.Indifferent),
            Note(5, pad: EliteDrumPad.Snare) });
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, 1.4, EliteDrumsAction.EliteClosedHiHat);
        Press(engine, 1.7, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
    }

    [Test]
    public void SplashStartsSustainAndCorrectClosedHitEndsIt()
    {
        var engine = Create(new[] { Note(1, 1920, EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash),
            Note(2, state: EliteDrumsHatState.Closed), Note(5, pad: EliteDrumPad.Snare) });
        Press(engine, 1, EliteDrumsAction.EliteSplash);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Press(engine, 1.9, EliteDrumsAction.EliteClosedHiHat);
        Press(engine, 1.95, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        engine.Reset();
        Press(engine, 1, EliteDrumsAction.EliteSplash);
        Press(engine, 1.4, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
    }

    [Test]
    public void RenewingOpenHitsDoNotBreakButTheirNewSustainCanBeBroken()
    {
        var engine = Create(new[] { Note(1), Note(1.5), Note(5, pad: EliteDrumPad.Snare) });
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, 1.45, EliteDrumsAction.EliteOpenHiHat);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        Press(engine, 1.7, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
    }

    [Test]
    public void MissingGeneratorOrResettingLeavesPedalFree()
    {
        var engine = Create(new[] { Note(1), Note(5, pad: EliteDrumPad.Snare) });
        Press(engine, 1.2, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        engine.Reset();
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        engine.Reset();
        Press(engine, 1.2, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [Test]
    public void InvisiblePedalAndVeryShortZonesDoNotProduceFaults()
    {
        var engine = Create(new[] { Note(1), Note(1.1, pad: EliteDrumPad.HatPedal,
            pedal: EliteDrumsHatPedalType.InvisibleTerminator), Note(5, pad: EliteDrumPad.Snare) });
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, 1.05, EliteDrumsAction.EliteStomp);
        Press(engine, 1.2, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClosureExperimentCannotForgiveOrDoubleCountAnActiveSustainFault(bool pedalFirst)
    {
        var engine = Create(new[] { Note(1, 1920), Note(5, pad: EliteDrumPad.Snare) }, closure: true);
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        engine.Update(1.04);
        var actions = pedalFirst ? new[] { EliteDrumsAction.EliteStomp, EliteDrumsAction.EliteClosedHiHat }
            : new[] { EliteDrumsAction.EliteClosedHiHat, EliteDrumsAction.EliteStomp };
        foreach (var action in actions)
        {
            var input = GameInput.Create(1.2, action, .8f);
            engine.QueueInput(ref input);
        }
        engine.Update(1.24);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(engine.EngineStats.OverhitsByAction.GetValueOrDefault((int)EliteDrumsAction.EliteStomp), Is.EqualTo(1));
    }

    [Test]
    public void AuthoredLengthsAndFadeBoundariesSurviveTempoChanges()
    {
        var notes = new[] { Note(1, 1920) };
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new(notes), new(), new());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        sync.Tempos.Add(new TempoChange(60, 2, 1920));
        var sustain = EliteHiHatSustain.Generate(chart, sync).Single();
        Assert.Multiple(() =>
        {
            Assert.That(sustain.StartTime, Is.EqualTo(1));
            Assert.That(sustain.EndTick, Is.EqualTo(2880));
            Assert.That(sustain.EndTime, Is.EqualTo(4));
            Assert.That(sustain.FadeStartTime, Is.EqualTo(3.5));
            Assert.That(notes[0].SourceDefinition!.EndTick, Is.EqualTo(2880));
        });
    }

    [Test]
    public void ExactlyEighthAuthoredLengthIsEntirelyFadeAndExactlySixteenthRunIsOmitted()
    {
        var engine = Create(new[] { Note(1, 240), Note(4, pad: EliteDrumPad.Snare) });
        var sustain = engine.HiHatSustains.Single();
        Assert.That(sustain.StartTick, Is.EqualTo(sustain.FadeStartTick));
        Assert.That(sustain.EndTime, Is.EqualTo(1.25));
        engine = Create(new[] { Note(1), Note(1.125, state: EliteDrumsHatState.Closed) });
        Assert.That(engine.HiHatSustains, Is.Empty);
    }

    [Test]
    public void ShortRenewalsInALongerConnectedRunRemainVisibleAndRenewable()
    {
        var engine = Create(new[] { Note(1), Note(1.0625), Note(1.25, state: EliteDrumsHatState.Closed) });
        Assert.That(engine.HiHatSustains, Has.Count.EqualTo(2));
        Assert.That(engine.HiHatSustains.All(sustain => !sustain.HasFade), Is.True);
    }

    [Test]
    public void EmptyPedalRollMarkerDoesNotDisableSustainEnforcement()
    {
        var engine = Create(new[] { Note(1, 1920), Note(5, pad: EliteDrumPad.Snare) });
        // An empty marker is not a playable roll and cannot grant protection.
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new() { Note(1, 1920), Note(5, pad: EliteDrumPad.Snare) }, new(), new());
        chart.Phrases.Add(new Phrase(PhraseType.EliteDrums_HatPedalLane, 1, 2, 960, 1920));
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        engine = new EliteDrumsEngine(chart, sync, (DrumsEngineParameters)engine.BaseParameters, false, true);
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, 1.4, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(engine.ActiveHiHatSustain, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SimultaneousOpenAndSplashAreSafeInEitherInputOrder(bool splashFirst)
    {
        var open = Note(1, 240);
        open.AddChildNote(Note(1, 0, EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash));
        var engine = Create(new[] { open, Note(5, pad: EliteDrumPad.Snare) });
        foreach (var action in splashFirst
            ? new[] { EliteDrumsAction.EliteSplash, EliteDrumsAction.EliteOpenHiHat }
            : new[] { EliteDrumsAction.EliteOpenHiHat, EliteDrumsAction.EliteSplash })
            Press(engine, 1, action);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        Assert.That(engine.HiHatSustains.Single().EndTime, Is.EqualTo(1.75),
            "Choose the longer independent duration after default generation, rather than the longest raw MIDI note.");
    }

    [Test]
    public void RingingIntoALongRestStillRequiresKeepingThePedalOpen()
    {
        var engine = Create(new[] { Note(1), Note(12, pad: EliteDrumPad.Snare) });
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, 1.2, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
    }

    [TestCase(0u, 1.75, true)]
    [TestCase(1920u, 3, true)]
    [TestCase(3840u, 5, false)]
    public void SustainEndDelaysCountdownOrRemovesAGapThatIsNoLongerLongEnough(
        uint length, double end, bool countdownAfter)
    {
        var engine = Create(new[] { Note(1, length), Note(12, pad: EliteDrumPad.Snare) });
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        engine.Update(end - .01);
        Assert.That(engine.IsWaitCountdownActive, Is.False, "A charted ringing interval remains note activity.");
        engine.Update(end + .01);
        Assert.That(engine.IsWaitCountdownActive, Is.EqualTo(countdownAfter));
    }

    [Test]
    public void ValidLaterChordGeneratorCanRestartAnEarlierBrokenRing()
    {
        var open = Note(1, 1920);
        open.AddChildNote(Note(1, 1920, EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash));
        var engine = Create(new[] { open, Note(5, pad: EliteDrumPad.Snare) });
        Press(engine, .92, EliteDrumsAction.EliteSplash);
        Press(engine, .94, EliteDrumsAction.EliteStomp);
        Assert.That(engine.WasHiHatSustainBroken(engine.HiHatSustains.Single()), Is.True);
        Press(engine, 1.02, EliteDrumsAction.EliteOpenHiHat);
        Assert.That(engine.ActiveHiHatSustain, Is.Not.Null);
        Assert.That(engine.WasHiHatSustainBroken(engine.HiHatSustains.Single()), Is.False);
    }

    [Test]
    public void EarlyHitBeforeCountdownEndStillRequiresKeepingItsRingOpen()
    {
        var engine = Create(new[] { Note(10, 1920) });
        Press(engine, 9.92, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, 9.95, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
    }

    [Test]
    public void IneligibleLateAssistedPedalCannotForgiveAClosure()
    {
        var open = Note(1, 1920);
        open.AddChildNote(Note(1, 1920, EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash));
        var engine = Create(new[] { open, Note(5, pad: EliteDrumPad.Snare) }, assisted: true);
        Press(engine, .92, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, 1.02, EliteDrumsAction.EliteSplash);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
    }

    [TestCase(1.05, 1)]
    [TestCase(1.4, 0)]
    public void OnlyThePlayablePedalRollSpanWaivesClosures(double close, int expected)
    {
        var engine = PedalRollEngine(false);
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        if (close > 1.2) Press(engine, 1.2, EliteDrumsAction.EliteSplash);
        Press(engine, close, EliteDrumsAction.EliteStomp);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(expected));
    }

    [Test]
    public void ClosurePairInsidePedalRollRetainsRollForgiveness()
    {
        var engine = PedalRollEngine(true);
        Press(engine, 1.2, EliteDrumsAction.EliteSplash);
        engine.Update(1.24);
        foreach (var action in new[] { EliteDrumsAction.EliteStomp, EliteDrumsAction.EliteClosedHiHat })
        {
            var input = GameInput.Create(1.4, action, .8f);
            engine.QueueInput(ref input);
        }
        engine.Update(1.44);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        Assert.That(engine.ActiveHiHatSustain, Is.Not.Null);
    }

    internal static EliteDrumsEngine PedalRollEngine(bool closure, bool current = false)
    {
        var first = Note(1.2, 1920, EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash);
        var last = Note(2.5, 0, EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash);
        var notes = new[] { Note(1, 1920), first, last, Note(5, pad: EliteDrumPad.Snare) };
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            notes.ToList(), new(), new());
        chart.Phrases.Add(new Phrase(PhraseType.EliteDrums_HatPedalLane, 1, 2, 960, 1920));
        chart.SetEliteDrumNativeAuthoredLaneRecords(new[] {
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_HatPedalLane, 960, 2880,
                new[] { first.SourceDefinition!, last.SourceDefinition! }) });
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var template = Create(new[] { Note(1) }, closure: closure);
        return new EliteDrumsEngine(chart, sync, current ? EliteHiHatSustainScoreTests.Parameters(closure: closure)
            : (DrumsEngineParameters)template.BaseParameters, false, true);
    }
}
