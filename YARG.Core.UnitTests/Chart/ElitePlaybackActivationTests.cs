// pattern: Imperative Shell
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Chart;

public sealed class ElitePlaybackActivationTests
{
    private static EliteDrumNote Note(EliteDrumPad pad, double time) =>
        new(pad, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent,
            EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumsChannelFlag.None, time, (uint) (time * 960), false);

    private static SongChart Source()
    {
        var end = Note(EliteDrumPad.Tom1, 3);
        end.AddChildNote(Note(EliteDrumPad.LeftCrash, 3));
        var native = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new() { Note(EliteDrumPad.Snare, 1), Note(EliteDrumPad.Snare, 2), end }, new(), new());
        native.Phrases.Add(new Phrase(PhraseType.StarPower, .9, .2, 864, 192));
        native.Phrases.Add(new Phrase(PhraseType.DrumFill, 1.5, 1.5, 1440, 1440));
        native.Phrases.Add(new Phrase(PhraseType.StarPower, 1.9, .2, 1824, 192));
        var chart = new SongChart(480);
        chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));
        chart.SyncTrack.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 0, 0));
        chart.AuthoredDrumSources.Add(new AuthoredDrumSourceTier(
            new DrumSourceTierFacts(DrumSourceFormat.Elite, Difficulty.Expert, false, true, false, false, true),
            null, native, System.Array.Empty<uint>()));
        return chart;
    }

    private static InstrumentDifficulty<EliteDrumNote> Prepare(SongChart chart) =>
        DrumPlaybackPreparer.Prepare(chart, new ResolvedDrumPlayback(Instrument.EliteDrums,
            DrumSourceFormat.Elite, Difficulty.Expert, Difficulty.Expert, DrumExtraKickPolicy.Remove,
            false, DrumScoreCategory.NativeElite)).Elite!;

    private static void Strike(EliteDrumsEngine engine, double time, EliteDrumsAction action)
    {
        var input = GameInput.Create(time, action, .8f);
        engine.QueueInput(ref input);
        engine.Update(time);
    }

    [Test]
    public void PreparingEliteDoesNotChooseAnActivationInsideTheFill()
    {
        var notes = Prepare(Source());
        Assert.That(notes.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Any(n => n.IsStarPowerActivator),
            Is.False, "Activation selection belongs to the player's setting and the fill end, not its final interior note.");
    }

    [TestCase(StarPowerActivationType.RightmostNote)]
    [TestCase(StarPowerActivationType.AllNotes)]
    public void EarningStarPowerInsideFillWaitsForItsBoundaryAfterEngineReset(StarPowerActivationType activation)
    {
        var chart = Source();
        var notes = Prepare(chart);
        notes.SetDrumActivationFlags(activation);
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.Elite);
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, false, true);

        for (int play = 0; play < 2; play++)
        {
            engine.Reset();
            Strike(engine, 1, EliteDrumsAction.EliteSnare);
            Strike(engine, 2, EliteDrumsAction.EliteSnare);
            Assert.That(engine.CanStarPowerActivate, Is.True, "Two phrases have earned enough SP.");
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.False,
                "The last interior note must not deploy SP just because it completed an award.");
            Strike(engine, 3, EliteDrumsAction.EliteTom1);
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.False, "The cymbal is still required.");
            Strike(engine, 3, EliteDrumsAction.EliteLeftCrash);
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.True);
        }
    }

    [TestCase(StarPowerActivationType.RightmostNote, false)]
    [TestCase(StarPowerActivationType.RightmostNote, true)]
    [TestCase(StarPowerActivationType.AllNotes, false)]
    [TestCase(StarPowerActivationType.AllNotes, true)]
    public void SkippingEligibleActivationPreservesScoreWithoutDeploying(
        StarPowerActivationType activation, bool partialChord)
    {
        var chart = Source();
        var notes = Prepare(chart);
        notes.SetDrumActivationFlags(activation);
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.Elite);
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, false, true);
        int hits = 0, misses = 0, pads = 0;
        engine.OnNoteHit += (_, _) => hits++;
        engine.OnNoteMissed += (_, _) => misses++;
        engine.OnPadHit += (_, _, _, _, _, _) => pads++;

        for (int play = 0; play < 2; play++)
        {
            engine.Reset();
            hits = misses = pads = 0;
            Strike(engine, 1, EliteDrumsAction.EliteSnare);
            Strike(engine, 2, EliteDrumsAction.EliteSnare);
            var amount = engine.EngineStats.StarPowerTickAmount;
            Assert.That(engine.CanStarPowerActivate, Is.True);
            // Rightmost still requires the ordinary tom. All Notes can skip either
            // the entire activation chord or just the cymbal after playing the tom.
            bool playTom = partialChord || activation == StarPowerActivationType.RightmostNote;
            if (playTom) Strike(engine, 3, EliteDrumsAction.EliteTom1);
            engine.Update(3.5);
            Assert.Multiple(() =>
            {
                Assert.That(notes.Notes[2].WasFullyHit(), Is.True);
                Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4));
                Assert.That(engine.EngineStats.Combo, Is.EqualTo(4));
                Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(200));
                Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(200));
                Assert.That(engine.EngineStats.StarPowerTickAmount, Is.EqualTo(amount));
                Assert.That(engine.EngineStats.IsStarPowerActive, Is.False);
                Assert.That(engine.CanStarPowerActivate, Is.True);
                Assert.That(engine.NoteIndex, Is.EqualTo(3));
                Assert.That(misses, Is.Zero);
                Assert.That(hits, Is.EqualTo(playTom ? 3 : 2));
                Assert.That(pads, Is.EqualTo(playTom ? 3 : 2));
            });
        }
    }

    [TestCase(StarPowerActivationType.RightmostNote, false)]
    [TestCase(StarPowerActivationType.RightmostNote, true)]
    [TestCase(StarPowerActivationType.AllNotes, false)]
    [TestCase(StarPowerActivationType.AllNotes, true)]
    public void SkippingActivationWithoutEnoughStarPowerStillMisses(
        StarPowerActivationType activation, bool singleNote)
    {
        var chart = Source();
        var notes = Prepare(chart);
        if (singleNote) notes.Notes[2].ChildNotes.Clear();
        notes.SetDrumActivationFlags(activation);
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.Elite);
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, false, true);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        engine.Update(3.5);
        Assert.Multiple(() =>
        {
            Assert.That(engine.CanStarPowerActivate, Is.False);
            Assert.That(notes.Notes[2].WasMissed, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.Combo, Is.Zero);
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.False);
        });
    }

    [TestCase(StarPowerActivationType.RightmostNote)]
    [TestCase(StarPowerActivationType.AllNotes)]
    public void SkippingSingleActivationDoesNotForgiveOrdinaryNote(StarPowerActivationType activation)
    {
        var chart = Source();
        var notes = Prepare(chart);
        notes.Notes[2].ChildNotes.Clear();
        notes.Notes.Add(Note(EliteDrumPad.Snare, 4));
        notes.SetDrumActivationFlags(activation);
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.Elite);
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, false, true);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        Strike(engine, 2, EliteDrumsAction.EliteSnare);
        engine.Update(3.5);
        Assert.That(notes.Notes[2].WasHit, Is.True);
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(3));
        engine.Update(4.5);
        Assert.Multiple(() =>
        {
            Assert.That(notes.Notes[3].WasMissed, Is.True);
            Assert.That(engine.EngineStats.Combo, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.False);
        });
    }

    [Test]
    public void RightmostSkipDoesNotForgiveUnflaggedChordMember()
    {
        var chart = Source();
        var notes = Prepare(chart);
        notes.SetDrumActivationFlags(StarPowerActivationType.RightmostNote);
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.Elite);
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, false, true);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        Strike(engine, 2, EliteDrumsAction.EliteSnare);
        engine.Update(3.5);
        Assert.Multiple(() =>
        {
            Assert.That(notes.Notes[2].WasMissed, Is.True, "The unflagged tom remains required.");
            Assert.That(notes.Notes[2].ChildNotes[0].WasHit, Is.True, "Only the activation cymbal is optional.");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.False);
        });
    }

    [TestCase(StarPowerActivationType.RightmostNote)]
    [TestCase(StarPowerActivationType.AllNotes)]
    public void BotStillDeploysAtActivationChordAfterReset(StarPowerActivationType activation)
    {
        var chart = Source();
        var notes = Prepare(chart);
        notes.SetDrumActivationFlags(activation);
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.Elite);
        var engine = new EliteDrumsEngine(notes, chart.SyncTrack, parameters, true, true);
        for (int play = 0; play < 2; play++)
        {
            engine.Reset();
            engine.Update(1);
            engine.Update(2);
            Assert.That(engine.CanStarPowerActivate, Is.True);
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.False);
            engine.Update(3);
            Assert.That(engine.EngineStats.IsStarPowerActive, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4));
        }
    }

    [Test]
    public void RebuildClearsObsoleteEliteDrumFlagsFromResetAndCloneState()
    {
        var note = Note(EliteDrumPad.Snare, 1);
        note.ActivateFlag(DrumNoteFlags.StarPowerActivator | DrumNoteFlags.KickLaneStart | DrumNoteFlags.KickLaneEnd);
        var notes = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new() { note }, new(), new());
        DrumPlaybackPreparer.Rebuild(notes);
        Assert.That(note.DrumFlags, Is.EqualTo(DrumNoteFlags.None));
        note.ResetNoteState();
        Assert.That(note.DrumFlags, Is.EqualTo(DrumNoteFlags.None), "Engine reset must not resurrect removed control flags.");
        Assert.That(note.Clone().DrumFlags, Is.EqualTo(DrumNoteFlags.None));
    }
}
