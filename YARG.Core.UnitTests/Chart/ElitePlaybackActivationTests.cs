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
