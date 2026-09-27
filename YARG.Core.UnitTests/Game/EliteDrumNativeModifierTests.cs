using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Game;

public sealed class EliteDrumNativeModifierTests
{
    private static EliteDrumNote MakeNote(EliteDrumNote.EliteDrumPad pad, DrumNoteType dynamics,
        double time, EliteDrumNote.EliteDrumsHatPedalType pedal = EliteDrumNote.EliteDrumsHatPedalType.Stomp)
    {
        return new EliteDrumNote(pad, dynamics, EliteDrumNote.EliteDrumsHatState.Indifferent, pedal,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumNote.EliteDrumsChannelFlag.None,
            time, (uint) (time * 480), false);
    }

    private static InstrumentDifficulty<EliteDrumNote> CreateEliteDifficulty(params EliteDrumNote[] notes)
    {
        return new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new(notes), new(), new());
    }

    [Test]
    public void EliteModifiersApplyToGameplayCloneWithoutMutatingSource()
    {
        var kick = MakeNote(EliteDrumNote.EliteDrumPad.Kick, DrumNoteType.Accent, 1);
        var snare = MakeNote(EliteDrumNote.EliteDrumPad.Snare, DrumNoteType.Ghost, 2);
        kick.NextNote = snare;
        snare.PreviousNote = kick;
        var source = CreateEliteDifficulty(kick, snare);
        var gameplay = source.Clone();
        var profile = new YargProfile { GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.EliteDrums };
        profile.AddSingleModifier(Modifier.NoKicks);
        profile.AddSingleModifier(Modifier.NoDynamics);

        profile.ApplyModifiers(gameplay, new SyncTrack(480));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gameplay.Notes, Has.Count.EqualTo(1));
            Assert.That(gameplay.Notes[0].Pad, Is.EqualTo((int) EliteDrumNote.EliteDrumPad.Snare));
            Assert.That(gameplay.Notes[0].Dynamics, Is.EqualTo(DrumNoteType.Neutral));
            Assert.That(gameplay.Notes[0].PreviousNote, Is.Null);
            Assert.That(gameplay.Notes[0].NextNote, Is.Null);
            Assert.That(source.Notes, Has.Count.EqualTo(2));
            Assert.That(source.Notes[0].Pad, Is.EqualTo((int) EliteDrumNote.EliteDrumPad.Kick));
            Assert.That(source.Notes[0].Dynamics, Is.EqualTo(DrumNoteType.Accent));
            Assert.That(source.Notes[1].Dynamics, Is.EqualTo(DrumNoteType.Ghost));
        }
    }

    [Test]
    public void EliteModifiersApplyToChordMembers()
    {
        var kick = MakeNote(EliteDrumNote.EliteDrumPad.Kick, DrumNoteType.Neutral, 1);
        var snare = MakeNote(EliteDrumNote.EliteDrumPad.Snare, DrumNoteType.Accent, 1);
        kick.AddChildNote(snare);
        var difficulty = CreateEliteDifficulty(kick);
        var profile = new YargProfile { GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.EliteDrums };
        profile.AddSingleModifier(Modifier.NoKicks);
        profile.AddSingleModifier(Modifier.NoDynamics);

        profile.ApplyModifiers(difficulty, new SyncTrack(480));

        Assert.That(difficulty.Notes, Has.Count.EqualTo(1));
        Assert.That(difficulty.Notes[0].Pad, Is.EqualTo((int) EliteDrumNote.EliteDrumPad.Snare));
        Assert.That(difficulty.Notes[0].Dynamics, Is.EqualTo(DrumNoteType.Neutral));
    }

    [Test]
    public void OrdinaryDrumModeStillAppliesNativeModifiers()
    {
        var kick = new DrumNote(FourLaneDrumPad.Kick, DrumNoteType.Accent, DrumNoteFlags.None,
            NoteFlags.None, 1, 480);
        var snare = new DrumNote(FourLaneDrumPad.RedDrum, DrumNoteType.Ghost, DrumNoteFlags.None,
            NoteFlags.None, 2, 960);
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.FourLaneDrums, Difficulty.Expert,
            new() { kick, snare }, new(), new());
        var profile = new YargProfile { GameMode = GameMode.FourLaneDrums };
        profile.AddSingleModifier(Modifier.NoKicks);
        profile.AddSingleModifier(Modifier.NoDynamics);

        profile.ApplyModifiers(difficulty, new SyncTrack(480));

        Assert.That(difficulty.Notes, Has.Count.EqualTo(1));
        Assert.That(difficulty.Notes[0].Type, Is.EqualTo(DrumNoteType.Neutral));
    }

    [TestCase(Instrument.ProDrums)]
    [TestCase(Instrument.FourLaneDrums)]
    [TestCase(Instrument.FiveLaneDrums)]
    public void EliteModeResolvedFallbackRetainsOrdinaryModifiers(Instrument instrument)
    {
        var kick = new DrumNote(FourLaneDrumPad.Kick, DrumNoteType.Accent, DrumNoteFlags.None,
            NoteFlags.None, 1, 480);
        var difficulty = new InstrumentDifficulty<DrumNote>(instrument, Difficulty.Expert,
            new() { kick }, new(), new());
        var profile = new YargProfile { GameMode = GameMode.EliteDrums,
            PreferredInstrument = Instrument.EliteDrums, CurrentInstrument = instrument };
        profile.AddSingleModifier(Modifier.NoDynamics);
        profile.ApplyModifiers(difficulty, new SyncTrack(480));
        Assert.That(kick.Type, Is.EqualTo(DrumNoteType.Neutral));
    }

    [Test]
    public void EliteDrumModeRejectsOrdinaryDrumDifficulty()
    {
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert,
            new(), new(), new());
        var profile = new YargProfile { GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.EliteDrums };

        Assert.Throws<System.InvalidOperationException>(() => profile.ApplyModifiers(difficulty, new SyncTrack(480)));
    }

    [TestCase(StarPowerActivationType.RightmostNote)]
    [TestCase(StarPowerActivationType.AllNotes)]
    public void EliteActivationDoesNotFlagInvisibleTerminator(StarPowerActivationType activationType)
    {
        var fillNote = MakeNote(EliteDrumNote.EliteDrumPad.Snare, DrumNoteType.Neutral, 1);
        var terminator = MakeNote(EliteDrumNote.EliteDrumPad.HatPedal, DrumNoteType.Neutral, 2,
            EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator);
        fillNote.NextNote = terminator;
        terminator.PreviousNote = fillNote;
        var difficulty = CreateEliteDifficulty(fillNote, terminator);
        difficulty.Phrases.Add(new Phrase(PhraseType.DrumFill, 0, 2, 0, 960));

        difficulty.SetDrumActivationFlags(activationType);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fillNote.IsStarPowerActivator, Is.False);
            Assert.That(terminator.IsStarPowerActivator, Is.False);
        }
    }

    [TestCase(StarPowerActivationType.RightmostNote)]
    [TestCase(StarPowerActivationType.AllNotes)]
    public void EliteActivationFlagsOnlyPlayableMembersAtBoundary(StarPowerActivationType activationType)
    {
        var control = MakeNote(EliteDrumNote.EliteDrumPad.HatPedal, DrumNoteType.Neutral, 2,
            EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator);
        var snare = MakeNote(EliteDrumNote.EliteDrumPad.Snare, DrumNoteType.Neutral, 2);
        control.AddChildNote(snare);
        var difficulty = CreateEliteDifficulty(control);
        difficulty.Phrases.Add(new Phrase(PhraseType.DrumFill, 0, 2, 0, 960));

        difficulty.SetDrumActivationFlags(activationType);

        Assert.That(control.IsStarPowerActivator, Is.False);
        Assert.That(snare.IsStarPowerActivator, Is.True);
    }
}
