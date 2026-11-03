// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Engine;

public class ClassicDrumArticulationTests
{
    private static readonly float[] THRESHOLDS = [1, 2, 3, 4, 5, 6];
    private static DrumNote Note(double time, FourLaneDrumPad pad = FourLaneDrumPad.RedDrum,
        DrumNoteFlags flags = DrumNoteFlags.Flam, DrumNoteType type = DrumNoteType.Neutral) =>
        new(pad, type, flags, NoteFlags.None, time, (uint)(time * 960));

    private static YargDrumsEngine Create(DrumNoteFlags flags, bool bot = false) => Create(new[] { Note(1, flags: flags) }, bot);

    private static YargDrumsEngine Create(DrumNote[] notes, bool bot = false,
        DrumsEngineParameters.DrumMode mode = DrumsEngineParameters.DrumMode.ProFourLane)
    {
        for (int i = 1; i < notes.Length; i++)
        { notes[i - 1].NextNote = notes[i]; notes[i].PreviousNote = notes[i - 1]; }
        var chart = new InstrumentDifficulty<DrumNote>(mode == DrumsEngineParameters.DrumMode.FiveLane ? Instrument.FiveLaneDrums :
            mode == DrumsEngineParameters.DrumMode.NonProFourLane ? Instrument.FourLaneDrums : Instrument.ProDrums,
            Difficulty.Expert, new(notes), new(), new());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        sync.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 0, 0));
        var parameters = EnginePreset.Default.Drums.Create(THRESHOLDS, THRESHOLDS, mode);
        return new YargDrumsEngine(chart, sync, parameters, bot, false);
    }

    private static void Press(YargDrumsEngine engine, double time, DrumsAction action = DrumsAction.RedDrum, float velocity = .8f)
    {
        var input = GameInput.Create(time, action, velocity);
        engine.QueueInput(ref input);
        engine.Update(time);
    }

    [TestCase(16, false)]
    [TestCase(32, true)]
    public void HandFlam_TwoProUnitsWithOneLifecycle(int flag, bool flat)
    {
        var engine = Create((DrumNoteFlags)flag);
        int hits = 0;
        engine.OnNoteHit += (_, _) => hits++;
        Assert.That(engine.BaseNoteScore, Is.EqualTo(120));
        Press(engine, 1);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(flat ? 120 : 60));
        Press(engine, 1.01);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(120));
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Assert.That(hits, Is.EqualTo(1));
    }

    [Test]
    public void ClassicModesDoNotExposeRetiredKickDynamicsModifier()
    {
        var kickOnly = (Modifier)(1UL << 19);
        Assert.That(GameMode.FourLaneDrums.PossibleModifiers(Instrument.ProDrums).possible & kickOnly, Is.EqualTo(Modifier.None));
        Assert.That(GameMode.FiveLaneDrums.PossibleModifiers(Instrument.FiveLaneDrums).possible & kickOnly, Is.EqualTo(Modifier.None));
    }
    [TestCase(DrumsEngineParameters.DrumMode.NonProFourLane, 100)]
    [TestCase(DrumsEngineParameters.DrumMode.ProFourLane, 120)]
    [TestCase(DrumsEngineParameters.DrumMode.FiveLane, 100)]
    public void BotCompletesBothPotentialUnitsWithoutPhysicalFollowUp(DrumsEngineParameters.DrumMode mode, int score)
    {
        var engine = Create(new[] { Note(1) }, true, mode);
        int followups = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followups++;
        engine.Update(2);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(score));
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
        Assert.That(followups, Is.Zero);
    }

    [Test]
    public void MissingOptionalFollowUpDoesNotMissOrBreakCombo()
    {
        var engine = Create(DrumNoteFlags.Flam);
        Press(engine, 1);
        engine.Update(2);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(60));
        Assert.That(engine.EngineStats.NotesMissed, Is.Zero);
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
    }

    [Test]
    public void RealFollowingNoteWins_AndReplacesPendingOnThatPad()
    {
        var engine = Create(new[] { Note(1), Note(1.05, flags: DrumNoteFlags.None), Note(3, FourLaneDrumPad.BlueDrum, DrumNoteFlags.None) });
        int followups = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followups++;
        Press(engine, 1);
        Press(engine, 1.04);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(120));
        Press(engine, 1.045);
        Assert.That(followups, Is.Zero);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
    }

    [Test]
    public void WrongPadAndReleaseDoNotConsumeFollowUp_ThirdStrikeIsAnOverhit()
    {
        var engine = Create(new[] { Note(1), Note(3, FourLaneDrumPad.BlueDrum, DrumNoteFlags.None) });
        int followups = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followups++;
        Press(engine, 1);
        Press(engine, 1.01, DrumsAction.BlueDrum);
        Press(engine, 1.02, velocity: 0);
        Press(engine, 1.03);
        Assert.That(followups, Is.EqualTo(1));
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(120));
        Press(engine, 1.04);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(2));
        Assert.That(followups, Is.EqualTo(1));
    }

    [Test]
    public void KickCanCoexistWithAHandFlam_AndCannotBecomeAHandFlam()
    {
        var kick = Note(1, FourLaneDrumPad.Kick, DrumNoteFlags.Flam);
        kick.AddChildNote(Note(1));
        var engine = Create(new[] { kick });
        Assert.That(kick.IsHandFlam, Is.False);
        Assert.That(engine.BaseNoteScore, Is.EqualTo(240));
        Press(engine, 1, DrumsAction.Kick);
        Press(engine, 1.01);
        Press(engine, 1.02);
        Press(engine, 1.03, DrumsAction.Kick);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(240));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
    }

    [Test]
    public void FlamDynamicsAwardOnlyOnItsPrimaryStrike()
    {
        var engine = Create(new[] { Note(1, type: DrumNoteType.Accent) });
        Press(engine, 1, velocity: 1);
        Press(engine, 1.01, velocity: 1);
        Assert.That(engine.EngineStats.DynamicsBonus, Is.EqualTo(25));
        Assert.That(engine.EngineStats.AccentsHit, Is.EqualTo(1));
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(120));
    }

}