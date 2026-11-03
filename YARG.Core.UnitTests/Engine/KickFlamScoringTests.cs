// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Engine;

public sealed class KickFlamScoringTests
{
    private static readonly float[] THRESHOLDS = [1, 2, 3, 4, 5, 6];
    private static SyncTrack Sync()
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        sync.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 0, 0));
        return sync;
    }

    private static DrumsEngineParameters Parameters(DrumsEngineParameters.DrumMode mode, byte? ruleset = null) => new(
        new HitWindowSettings(.1, .1, 1, false, 0, 1, 1, .15, .25), 4, THRESHOLDS, THRESHOLDS,
        mode, false, true, classicArticulationRuleset: ruleset ?? EnginePreset.Default.Drums.Create(THRESHOLDS, THRESHOLDS, mode).ClassicArticulationRuleset);

    private static DrumNote ClassicNote(double time = 1, DrumNoteFlags flags = DrumNoteFlags.Flam,
        DrumNoteType dynamics = DrumNoteType.Neutral, FourLaneDrumPad pad = FourLaneDrumPad.Kick) =>
        new(pad, dynamics, flags, NoteFlags.None, time, (uint)(time * 960));

    private static EliteDrumNote EliteNote(double time = 1, bool flat = false,
        DrumNoteType dynamics = DrumNoteType.Neutral, bool flam = true,
        EliteDrumNote.EliteDrumPad pad = EliteDrumNote.EliteDrumPad.Kick) =>
        new(pad, dynamics, EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumNote.EliteDrumsHatPedalType.Stomp,
            flam, DrumNoteFlags.None, NoteFlags.None, EliteDrumNote.EliteDrumsChannelFlag.None,
            time, (uint)(time * 960), false, isFlatFlam: flat);

    private static YargDrumsEngine Classic(DrumsEngineParameters.DrumMode mode, bool bot = false, byte? ruleset = null,
        params DrumNote[] notes)
    {
        for (int i = 1; i < notes.Length; i++) { notes[i-1].NextNote = notes[i]; notes[i].PreviousNote = notes[i-1]; }
        var instrument = mode == DrumsEngineParameters.DrumMode.FiveLane ? Instrument.FiveLaneDrums :
            mode == DrumsEngineParameters.DrumMode.ProFourLane ? Instrument.ProDrums : Instrument.FourLaneDrums;
        return new YargDrumsEngine(new(instrument, Difficulty.Expert, new(notes), new(), new()), Sync(), Parameters(mode, ruleset), bot, false);
    }

    private static EliteDrumsEngine Elite(bool bot = false, byte? ruleset = null, params EliteDrumNote[] notes)
    {
        for (int i = 1; i < notes.Length; i++) { notes[i-1].NextNote = notes[i]; notes[i].PreviousNote = notes[i-1]; }
        return new EliteDrumsEngine(new(Instrument.EliteDrums, Difficulty.Expert, new(notes), new(), new()),
            Sync(), Parameters(DrumsEngineParameters.DrumMode.Elite, ruleset), bot, true);
    }

    private static void Press(YargDrumsEngine engine, double time, DrumsAction action = DrumsAction.Kick, float velocity = .8f)
    { var input = GameInput.Create(time, action, velocity); engine.QueueInput(ref input); engine.Update(time); }
    private static void Press(EliteDrumsEngine engine, double time, EliteDrumsAction action = EliteDrumsAction.Kick, float velocity = .8f)
    { var input = GameInput.Create(time, action, velocity); engine.QueueInput(ref input); engine.Update(time); }

    [TestCase(DrumsEngineParameters.DrumMode.NonProFourLane, 50)]
    [TestCase(DrumsEngineParameters.DrumMode.ProFourLane, 60)]
    [TestCase(DrumsEngineParameters.DrumMode.FiveLane, 50)]
    public void ClassicKickFlamAwardsOptionalSecondUnitWithOneLifecycle(DrumsEngineParameters.DrumMode mode, int unit)
    {
        var engine = Classic(mode, notes: [ClassicNote()]);
        int hits = 0, followups = 0;
        engine.OnNoteHit += (_, _) => hits++;
        engine.OnFlamFollowUp += (_, action, _, bonus) => { Assert.That(action, Is.EqualTo(DrumsAction.Kick)); Assert.That(bonus, Is.True); followups++; };
        Assert.That(engine.BaseNoteScore, Is.EqualTo(2 * unit));
        Press(engine, 1);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(unit));
        Press(engine, 1.01);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(2 * unit));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(followups, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EliteKickAlwaysUsesRegularFlamScoringAndKeepsDynamics(bool malformedFlatFlag)
    {
        var engine = Elite(notes: [EliteNote(flat: malformedFlatFlag, dynamics: DrumNoteType.Accent)]);
        int hits = 0, followups = 0;
        engine.OnNoteHit += (_, _) => hits++;
        engine.OnFlamFollowUp += (_, _, _, bonus) => { Assert.That(bonus, Is.True); followups++; };
        Assert.That(engine.BaseNoteScore, Is.EqualTo(100));
        Press(engine, 1, velocity: 1);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(50));
        Assert.That(engine.EngineStats.DynamicsBonus, Is.EqualTo(25));
        Press(engine, 1.01, velocity: 1);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(100));
            Assert.That(engine.EngineStats.DynamicsBonus, Is.EqualTo(25));
            Assert.That(engine.EngineStats.AccentsHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(followups, Is.EqualTo(1));
        });
    }

    [Test]
    public void ClassicFlatKickFlagStillRequiresOptionalStrikeForSecondUnit()
    {
        var engine = Classic(DrumsEngineParameters.DrumMode.ProFourLane, notes: [ClassicNote(flags: DrumNoteFlags.FlatFlam)]);
        Press(engine, 1);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(60));
        Press(engine, 1.01);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(120));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void HistoricalRulesetsKeepKickFlamsSingleUnit(int ruleset)
    {
        var classic = Classic(DrumsEngineParameters.DrumMode.ProFourLane, ruleset: (byte)ruleset, notes: [ClassicNote()]);
        var elite = Elite(ruleset: (byte)ruleset, notes: [EliteNote()]);
        Assert.That(classic.BaseNoteScore, Is.EqualTo(60));
        Assert.That(elite.BaseNoteScore, Is.EqualTo(50));
        Press(classic, 1); Press(classic, 1.01);
        Press(elite, 1); Press(elite, 1.01);
        Assert.That(classic.EngineStats.NoteScore, Is.EqualTo(60));
        Assert.That(elite.EngineStats.NoteScore, Is.EqualTo(50));
    }

    [Test]
    public void BotCompletesKickPotentialWithoutSyntheticFollowUp()
    {
        var classic = Classic(DrumsEngineParameters.DrumMode.ProFourLane, bot: true, notes: [ClassicNote()]);
        var elite = Elite(bot: true, notes: [EliteNote()]);
        int followed = 0;
        classic.OnFlamFollowUp += (_, _, _, _) => followed++;
        elite.OnFlamFollowUp += (_, _, _, _) => followed++;
        classic.Update(2); elite.Update(2);
        Assert.That(classic.EngineStats.NoteScore, Is.EqualTo(120));
        Assert.That(elite.EngineStats.NoteScore, Is.EqualTo(100));
        Assert.That(followed, Is.Zero);
    }

    [Test]
    public void MissingKickFollowUpDoesNotMissOrBreakCombo()
    {
        var classic = Classic(DrumsEngineParameters.DrumMode.ProFourLane, notes: [ClassicNote()]);
        var elite = Elite(notes: [EliteNote()]);
        Press(classic, 1); Press(elite, 1);
        classic.Update(2); elite.Update(2);
        Assert.That(classic.EngineStats.NotesMissed + elite.EngineStats.NotesMissed, Is.Zero);
        Assert.That(classic.EngineStats.Combo, Is.EqualTo(1));
        Assert.That(elite.EngineStats.Combo, Is.EqualTo(1));
    }

    [Test]
    public void RealKickReplacesOldFlamOpportunity()
    {
        var classic = Classic(DrumsEngineParameters.DrumMode.ProFourLane, notes: [ClassicNote(), ClassicNote(1.05, DrumNoteFlags.None), ClassicNote(3)]);
        var elite = Elite(notes: [EliteNote(), EliteNote(1.05, flam: false), EliteNote(3)]);
        int followed = 0;
        classic.OnFlamFollowUp += (_, _, _, _) => followed++;
        elite.OnFlamFollowUp += (_, _, _, _) => followed++;
        Press(classic, 1); Press(elite, 1);
        Press(classic, 1.04); Press(elite, 1.04);
        Assert.That(classic.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(elite.EngineStats.NotesHit, Is.EqualTo(2));
        Press(classic, 1.045); Press(elite, 1.045);
        Assert.That(followed, Is.Zero);
        Assert.That(classic.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(elite.EngineStats.Overhits, Is.EqualTo(1));
    }

    [TestCase(-.000001, true)]
    [TestCase(0, true)]
    [TestCase(.000001, false)]
    public void KickFollowUpUsesOriginalInclusiveBackBoundary(double offset, bool accepted)
    {
        var classic = Classic(DrumsEngineParameters.DrumMode.ProFourLane, notes: [ClassicNote(), ClassicNote(3)]);
        var elite = Elite(notes: [EliteNote(), EliteNote(3)]);
        double classicEnd = 1 + classic.CalculateHitWindow().BackEnd;
        double eliteEnd = 1 + elite.CalculateHitWindow().BackEnd;
        Press(classic, 1); Press(elite, 1);
        Press(classic, classicEnd + offset); Press(elite, eliteEnd + offset);
        Assert.That(classic.EngineStats.NoteScore, Is.EqualTo(accepted ? 120 : 60));
        Assert.That(elite.EngineStats.NoteScore, Is.EqualTo(accepted ? 100 : 50));
    }

    [Test]
    public void ResetClearsPendingKickAndAllowsNewPlaythrough()
    {
        var classic = Classic(DrumsEngineParameters.DrumMode.ProFourLane, notes: [ClassicNote(), ClassicNote(3)]);
        var elite = Elite(notes: [EliteNote(), EliteNote(3)]);
        Press(classic, 1); Press(elite, 1);
        classic.Reset(); elite.Reset();
        Press(classic, 1); Press(elite, 1);
        Press(classic, 1.01); Press(elite, 1.01);
        Assert.That(classic.EngineStats.NoteScore, Is.EqualTo(120));
        Assert.That(elite.EngineStats.NoteScore, Is.EqualTo(100));
        Assert.That(classic.EngineStats.Combo, Is.EqualTo(1));
        Assert.That(elite.EngineStats.Combo, Is.EqualTo(1));
    }

    [Test]
    public void SamePadKickLaneSuppressesFlamButUnrelatedHandLaneDoesNot()
    {
        var kick = ClassicNote(flags: DrumNoteFlags.Flam | DrumNoteFlags.KickLane);
        var ordinaryLane = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert, new() { kick }, new(), new());
        var classicCopy = ordinaryLane.Clone();
        DrumPlaybackPreparer.NormalizeClassicFlams(classicCopy);
        Assert.That(classicCopy.Notes[0].IsKickFlam, Is.False);
        Assert.That(kick.IsKickFlam, Is.True, "normalization must not mutate source objects");

        var eliteKick = EliteNote();
        var source = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert, new() { eliteKick }, new(), new());
        source.Phrases.Add(new Phrase(PhraseType.EliteDrums_KickLane, 1, 1, 960, 960));
        var copy = source.Clone();
        DrumPlaybackPreparer.NormalizeEliteFlams(copy);
        Assert.That(copy.Notes[0].IsKickFlam, Is.False);
        Assert.That(eliteKick.IsKickFlam, Is.True);
        source.Phrases.Clear();
        source.Phrases.Add(new Phrase(PhraseType.EliteDrums_SnareLane, 1, 1, 960, 960));
        DrumPlaybackPreparer.NormalizeEliteFlams(source);
        Assert.That(source.Notes[0].IsKickFlam, Is.True);
    }

    [TestCase(10)]
    [TestCase(20)]
    [TestCase(30)]
    public void KickFollowUpUsesCurrentMultiplierWithoutAdvancingCombo(int threshold)
    {
        var notes = new List<EliteDrumNote>();
        for (int i = 1; i < threshold - 1; i++) notes.Add(EliteNote(i, flam: false));
        notes.Add(EliteNote(threshold - 1));
        notes.Add(EliteNote(threshold - .96, flam: false, pad: EliteDrumNote.EliteDrumPad.Snare));
        notes.Add(EliteNote(threshold + 2, flam: false));
        var elite = Elite(notes: notes.ToArray());
        for (int i = 1; i < threshold - 1; i++) Press(elite, i);
        Press(elite, threshold - 1);
        Press(elite, threshold - .96, EliteDrumsAction.EliteSnare);
        int before = elite.EngineStats.CommittedScore;
        Press(elite, threshold - .95);
        Assert.That(elite.EngineStats.CommittedScore - before, Is.EqualTo(50 * (threshold / 10 + 1)));
        Assert.That(elite.EngineStats.Combo, Is.EqualTo(threshold));
        Assert.That(elite.EngineStats.NotesHit, Is.EqualTo(threshold));
    }

    [Test]
    public void CodaKickConsumesPendingOpportunityWithoutAwardingIt()
    {
        var first = EliteNote();
        var coda = EliteNote(1.02, flam: false);
        coda.ActivateFlag(NoteFlags.BigRockEnding);
        var notes = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new() { first, coda, EliteNote(4, flam: false) }, new(), new());
        notes.Phrases.Add(new Phrase(PhraseType.BigRockEnding, 1.01, 1, 970, 960));
        notes.Notes[2].ActivateFlag(NoteFlags.CodaEnd);
        first.NextNote = coda; coda.PreviousNote = first;
        coda.NextNote = notes.Notes[2]; notes.Notes[2].PreviousNote = coda;
        var engine = new EliteDrumsEngine(notes, Sync(), Parameters(DrumsEngineParameters.DrumMode.Elite), false, true);
        int followups = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followups++;
        Press(engine, 1);
        Press(engine, 1.02);
        Assert.That(coda.WasHit, Is.True);
        Press(engine, 1.03);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(50));
        Assert.That(followups, Is.Zero);
    }
}
