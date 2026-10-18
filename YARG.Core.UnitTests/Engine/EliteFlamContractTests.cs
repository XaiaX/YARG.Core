using System;
using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

public class EliteFlamContractTests
{
    private const double EPSILON = 0.000001;
    private static readonly float[] THRESHOLDS = [1f, 2f, 3f, 4f, 5f, 6f];

    private static EliteDrumNote Note(double time = 1, int kind = 1,
        EliteDrumNote.EliteDrumPad pad = EliteDrumNote.EliteDrumPad.Snare,
        NoteFlags flags = NoteFlags.None, DrumNoteFlags drumFlags = DrumNoteFlags.None,
        EliteDrumSourceDefinition? source = null) =>
        new(pad, DrumNoteType.Neutral, EliteDrumNote.EliteDrumsHatState.Indifferent,
            EliteDrumNote.EliteDrumsHatPedalType.Stomp, kind != 0, drumFlags, flags,
            EliteDrumNote.EliteDrumsChannelFlag.None, time, (uint) (time * 960), false,
            source, isFlatFlam: kind == 2);

    private static InstrumentDifficulty<EliteDrumNote> Chart(params EliteDrumNote[] notes)
    {
        for (int i = 1; i < notes.Length; i++)
        {
            notes[i - 1].NextNote = notes[i];
            notes[i].PreviousNote = notes[i - 1];
        }
        return new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new(notes), new(), new());
    }

    private static EliteDrumsEngine Create(InstrumentDifficulty<EliteDrumNote> chart,
        DrumsEngineParameters? parameters = null, bool bot = false)
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        sync.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 0, 0));
        parameters ??= EnginePreset.Default.Drums.Create(THRESHOLDS, THRESHOLDS,
            DrumsEngineParameters.DrumMode.ProFourLane);
        return new EliteDrumsEngine(chart, sync, parameters, bot, true);
    }

    private static void Press(EliteDrumsEngine engine, double time, params EliteDrumsAction[] actions)
    {
        foreach (var action in actions)
        {
            var input = GameInput.Create(time, action, 0.8f);
            engine.QueueInput(ref input);
        }
        engine.Update(time);
    }

    [TestCase(0, 10, 500, 500)]
    [TestCase(0, 20, 1500, 1000)]
    [TestCase(0, 30, 3000, 1500)]
    [TestCase(1, 10, 1000, 1000)]
    [TestCase(1, 20, 3000, 2000)]
    [TestCase(1, 30, 6000, 3000)]
    [TestCase(2, 10, 1000, 1000)]
    [TestCase(2, 20, 3000, 2000)]
    [TestCase(2, 30, 6000, 3000)]
    public void ChartMaximaCountFlamUnitsButComboThresholdsCountRealNotes(
        int kind, int count, int maximum, int baseNotes)
    {
        var notes = new List<EliteDrumNote>();
        for (int i = 0; i < count; i++) notes.Add(Note(1 + i, kind));
        var engine = Create(Chart(notes.ToArray()));
        Assert.Multiple(() =>
        {
            Assert.That(engine.BaseScore, Is.EqualTo(maximum));
            Assert.That(engine.BaseNoteScore, Is.EqualTo(baseNotes));
        });
        for (int i = 0; i < count; i++)
        {
            int before = engine.EngineStats.CommittedScore;
            Press(engine, 1 + i, EliteDrumsAction.EliteSnare);
            if (kind == 1) Press(engine, 1.01 + i, EliteDrumsAction.EliteSnare);
            int multiplier = Math.Min((i + 1) / 10 + 1, 4);
            Assert.Multiple(() =>
            {
                Assert.That(engine.EngineStats.Combo, Is.EqualTo(i + 1));
                Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(i + 1));
                Assert.That(engine.EngineStats.CommittedScore - before,
                    Is.EqualTo((kind == 0 ? 50 : 100) * multiplier), $"real note {i + 1}");
            });
        }
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(baseNotes));
    }

    [TestCase(10)]
    [TestCase(20)]
    [TestCase(30)]
    public void RegularExtraUnitUsesMultiplierAtFollowUpNotAtFirstStrike(int threshold)
    {
        var notes = new List<EliteDrumNote>();
        for (int i = 1; i < threshold - 1; i++) notes.Add(Note(i, 0));
        var flam = Note(threshold - 1);
        notes.Add(flam);
        notes.Add(Note(threshold - 0.96, 0, EliteDrumNote.EliteDrumPad.Tom1));
        notes.Add(Note(threshold + 2, 0));
        var engine = Create(Chart(notes.ToArray()));
        for (int i = 1; i < threshold - 1; i++) Press(engine, i, EliteDrumsAction.EliteSnare);
        Press(engine, flam.Time, EliteDrumsAction.EliteSnare);
        Press(engine, threshold - 0.96, EliteDrumsAction.EliteTom1);
        int before = engine.EngineStats.CommittedScore;
        Press(engine, threshold - 0.95, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.CommittedScore - before, Is.EqualTo(50 * (threshold / 10 + 1)));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(threshold));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(threshold));
        });
    }

    [Test]
    public void RegularExtraUnitUsesStarPowerActivatedBetweenStrikes()
    {
        var firstPhrase = Note(1, 0, flags: NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd);
        var secondPhrase = Note(2, 0, flags: NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd);
        var flam = Note(3);
        var activator = Note(3.02, 0, EliteDrumNote.EliteDrumPad.Tom1,
            drumFlags: DrumNoteFlags.StarPowerActivator);
        var engine = Create(Chart(firstPhrase, secondPhrase, flam, activator, Note(5, 0)));
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Press(engine, 2, EliteDrumsAction.EliteSnare);
        Assert.That(engine.CanStarPowerActivate, Is.True);
        Press(engine, 3, EliteDrumsAction.EliteSnare);
        Assert.That(engine.EngineStats.IsStarPowerActive, Is.False);
        Press(engine, 3.02, EliteDrumsAction.EliteTom1);
        Assert.That(engine.EngineStats.IsStarPowerActive, Is.True);
        int before = engine.EngineStats.CommittedScore;
        Press(engine, 3.03, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.CommittedScore - before, Is.EqualTo(100));
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(250));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(4));
        });
    }

    private static DrumsEngineParameters WindowParameters(bool precision) => precision
        ? EnginePreset.Precision.Drums.Create(THRESHOLDS, THRESHOLDS, DrumsEngineParameters.DrumMode.ProFourLane)
        : new DrumsEngineParameters(new HitWindowSettings(0.24, 0.24, 0.5, false,
            0.6, 1, 2, 0.16, 0.08), 4, THRESHOLDS, THRESHOLDS,
            DrumsEngineParameters.DrumMode.ProFourLane, false, true);

    [TestCase(false, -1)]
    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, -1)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    public void FrontBoundaryIsInclusiveAndDoesNotArmFollowUpBeforeWindow(bool precision, int offset)
    {
        var note = Note();
        var engine = Create(Chart(note, Note(5, 0)), WindowParameters(precision));
        var window = engine.CalculateHitWindow();
        double front = note.Time + window.FrontEnd;
        int followUps = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followUps++;
        Press(engine, front + offset * EPSILON, EliteDrumsAction.EliteSnare);
        Assert.That(note.WasHit, Is.EqualTo(offset >= 0));
        Press(engine, front + 2 * EPSILON, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(offset >= 0 ? 100 : 50));
            Assert.That(followUps, Is.EqualTo(offset >= 0 ? 1 : 0));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        });
    }

    [TestCase(false, -1)]
    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, -1)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    public void FollowUpUsesExactCalculatedBackBoundaryIncludingBeyond63Milliseconds(bool precision, int offset)
    {
        var note = Note();
        var engine = Create(Chart(note, Note(5, 0)), WindowParameters(precision));
        double end = note.Time + engine.CalculateHitWindow().BackEnd;
        Assert.That(end - note.Time, Is.GreaterThan(0.063));
        int followUps = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followUps++;
        Press(engine, note.Time, EliteDrumsAction.EliteSnare);
        Press(engine, end + offset * EPSILON, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(offset <= 0 ? 100 : 50));
            Assert.That(followUps, Is.EqualTo(offset <= 0 ? 1 : 0));
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(offset <= 0 ? 0 : 1));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(offset <= 0 ? 1 : 0));
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ThirdStrikeBeforeChartEndIsAnOverhitNotAnotherFollowUp(int kind)
    {
        var engine = Create(Chart(Note(kind: kind), Note(5, 0)));
        int followUps = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followUps++;
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Press(engine, 1.01, EliteDrumsAction.EliteSnare);
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
        Press(engine, 1.02, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(100));
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(100));
            Assert.That(engine.EngineStats.Combo, Is.Zero);
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
            Assert.That(followUps, Is.EqualTo(1));
        });
    }

    [Test]
    public void WrongPadDoesNotConsumePendingFlamAndUsesNormalOverhitPenalty()
    {
        var engine = Create(Chart(Note(), Note(5, 0)));
        int followUps = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followUps++;
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Press(engine, 1.01, EliteDrumsAction.EliteTom1);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(50));
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
            Assert.That(followUps, Is.Zero);
        });
        Press(engine, 1.02, EliteDrumsAction.EliteSnare);
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(100));
        Assert.That(followUps, Is.EqualTo(1));
    }

    [Test]
    public void SameTimestampStrikesConsumeEachPadsFollowUpExactlyOnce()
    {
        var snare = Note();
        var tom = Note(pad: EliteDrumNote.EliteDrumPad.Tom1);
        snare.AddChildNote(tom);
        var engine = Create(Chart(snare, Note(5, 0)));
        var followed = new List<EliteDrumNote>();
        engine.OnFlamFollowUp += (note, _, _, _) => followed.Add(note);
        Press(engine, 1, EliteDrumsAction.EliteSnare, EliteDrumsAction.EliteTom1,
            EliteDrumsAction.EliteSnare, EliteDrumsAction.EliteTom1);
        Assert.Multiple(() =>
        {
            Assert.That(followed, Is.EquivalentTo(new[] { snare, tom }));
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(200));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        });
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(followed.Count, Is.EqualTo(2));
    }

    [Test]
    public void SamePadCodaNoteInvalidatesOlderPendingFlam()
    {
        var flam = Note();
        var coda = Note(1.02, 0, flags: NoteFlags.BigRockEnding);
        var chart = Chart(flam, coda, Note(4, 0, flags: NoteFlags.CodaEnd));
        chart.Phrases.Add(new Phrase(PhraseType.BigRockEnding, 1.01, 1, 970, 960));
        var engine = Create(chart);
        int followUps = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followUps++;
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Press(engine, 1.02, EliteDrumsAction.EliteSnare);
        Assert.That(coda.WasHit, Is.True);
        Press(engine, 1.03, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(50));
            Assert.That(followUps, Is.Zero);
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void LaneNormalizationUsesExactSourceMembershipOnCloneAndRemovesExtraUnit(int kind)
    {
        var memberSource = new EliteDrumSourceDefinition("lane", 0,
            (int) EliteDrumNote.EliteDrumPad.Snare, 960, 0, 1);
        var independentSource = new EliteDrumSourceDefinition("lane", 1,
            (int) EliteDrumNote.EliteDrumPad.Snare, 960, 0, 1);
        var member = Note(kind: kind, source: memberSource);
        var independent = Note(kind: kind, source: independentSource);
        var source = Chart(member, independent, Note(5, 0));
        source.Phrases.Add(new Phrase(PhraseType.EliteDrums_SnareLane, 1, 1, 960, 960));
        source.SetEliteDrumNativeAuthoredLaneRecords(new[]
        {
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 1920, new[] { memberSource })
        });
        var copy = source.Clone();
        DrumPlaybackPreparer.NormalizeEliteFlams(copy);
        var members = copy.Notes;
        var engine = Create(copy);
        int followUps = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followUps++;
        Assert.Multiple(() =>
        {
            Assert.That(member.IsHandFlam, Is.True, "normalization must not mutate cached chart");
            Assert.That(independent.IsHandFlam, Is.True);
            Assert.That(members[0].IsHandFlam, Is.False);
            Assert.That(members[0].IsAuthoredFlam, Is.True);
            Assert.That(members[1].IsHandFlam, Is.True, "same time/pad is not source membership");
            Assert.That(engine.BaseNoteScore, Is.EqualTo(200));
            Assert.That(engine.BaseScore, Is.EqualTo(200));
        });
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(members[0].WasHit, Is.True);
            Assert.That(members[1].WasHit, Is.False, "independent collision requires another physical strike");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(50));
            Assert.That(followUps, Is.Zero);
        });
        Press(engine, 1.01, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(members[1].WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(kind == 2 ? 150 : 100));
            Assert.That(followUps, Is.Zero, "second strike belongs to the independent real note");
        });
        Press(engine, 1.02, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(150));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
            Assert.That(followUps, Is.EqualTo(1));
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void NormalizedLaneMemberDoesNotArmAnyFlamFollowUp(int kind)
    {
        var memberSource = new EliteDrumSourceDefinition("single-lane", 0,
            (int) EliteDrumNote.EliteDrumPad.Snare, 960, 0, 1);
        var source = Chart(Note(kind: kind, source: memberSource), Note(5, 0));
        source.Phrases.Add(new Phrase(PhraseType.EliteDrums_SnareLane, 1, 1, 960, 960));
        source.SetEliteDrumNativeAuthoredLaneRecords(new[]
        {
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 1920, new[] { memberSource })
        });
        var copy = source.Clone();
        DrumPlaybackPreparer.NormalizeEliteFlams(copy);
        var engine = Create(copy);
        int followUps = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followUps++;
        Press(engine, 1, EliteDrumsAction.EliteSnare);
        Press(engine, 1.01, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(engine.BaseNoteScore, Is.EqualTo(100));
            Assert.That(engine.BaseScore, Is.EqualTo(100));
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(50));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(followUps, Is.Zero);
            Assert.That(source.Notes[0].IsHandFlam, Is.True);
        });
    }

    [TestCase(0.001)]
    [TestCase(0.017)]
    [TestCase(0.2)]
    public void BotCompletesAdjacentAndFinalFlamsWithoutSyntheticFollowUps(double frameStep)
    {
        var notes = new[] { Note(1), Note(1.04, 0), Note(1.08, 2), Note(1.12) };
        var engine = Create(Chart(notes), bot: true);
        int followUps = 0, hits = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followUps++;
        engine.OnNoteHit += (_, _) => hits++;
        for (double time = 0; time < 1.5; time += frameStep) engine.Update(time);
        engine.Update(2);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(350));
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(350));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(4));
            Assert.That(hits, Is.EqualTo(4));
            Assert.That(followUps, Is.Zero);
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        });
    }
}
