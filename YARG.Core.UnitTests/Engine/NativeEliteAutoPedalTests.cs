using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

public sealed class NativeEliteAutoPedalTests
{
    private static EliteDrumNote Note(EliteDrumNote.EliteDrumPad pad, double time = 1,
        EliteDrumNote.EliteDrumsHatPedalType pedal = EliteDrumNote.EliteDrumsHatPedalType.Stomp) =>
        new(pad, DrumNoteType.Neutral, EliteDrumNote.EliteDrumsHatState.Indifferent,
            pedal, false, DrumNoteFlags.None, NoteFlags.None, EliteDrumNote.EliteDrumsChannelFlag.None,
            time, (uint) (time * 960), false);

    private static EliteDrumsEngine Create(bool auto, params EliteDrumNote[] notes)
    {
        for (int i = 1; i < notes.Length; i++)
        {
            notes[i - 1].NextNote = notes[i];
            notes[i].PreviousNote = notes[i - 1];
        }
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums,
            Difficulty.Expert, new(notes), new(), new());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.ProFourLane);
        return new EliteDrumsEngine(chart, sync, parameters, false, true, auto);
    }

    private static void Queue(EliteDrumsEngine engine, double time, EliteDrumsAction action)
    {
        var input = GameInput.Create(time, action, 0.8f);
        engine.QueueInput(ref input);
    }

    [Test]
    public void DefaultAndScoringIsolation()
    {
        var pedal = Note(EliteDrumNote.EliteDrumPad.HatPedal);
        var engine = Create(true, pedal);
        int assisted = 0;
        engine.OnPedalAssisted += _ => assisted++;
        engine.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(pedal.WasHit, Is.True);
            Assert.That(engine.EngineStats.TotalNotes, Is.Zero);
            Assert.That(engine.EngineStats.OptionalPedalNotes, Is.EqualTo(1));
            Assert.That(engine.EngineStats.AssistedPedalNotes, Is.EqualTo(1));
            Assert.That(engine.EngineStats.NotesHit, Is.Zero);
            Assert.That(engine.EngineStats.CommittedScore, Is.Zero);
            Assert.That(engine.EngineStats.Combo, Is.Zero);
            Assert.That(engine.EngineStats.Percent, Is.EqualTo(1));
            Assert.That(engine.BaseScore, Is.EqualTo(50));
            Assert.That(assisted, Is.EqualTo(1));
        });
        engine.Update(2);
        Assert.That(assisted, Is.EqualTo(1));
    }

    [Test]
    public void AssistedPedalAloneDoesNotAwardStarPowerPhrase()
    {
        var pedal = Note(EliteDrumNote.EliteDrumPad.HatPedal);
        pedal.Flags |= NoteFlags.StarPower | NoteFlags.StarPowerEnd;
        var engine = Create(true, pedal);
        engine.Update(1);
        Assert.That(engine.EngineStats.StarPowerPhrasesHit, Is.Zero);
        Assert.That(engine.EngineStats.AssistedPedalNotes, Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SameTimestampHatFirstPedalFirstAndMultiplePedals(bool hatFirst)
    {
        var pedal = Note(EliteDrumNote.EliteDrumPad.HatPedal);
        var hat = Note(EliteDrumNote.EliteDrumPad.HiHat);
        pedal.AddChildNote(hat);
        var engine = Create(true, pedal);
        Assert.That(hat.Parent, Is.SameAs(pedal));
        if (hatFirst)
        {
            Queue(engine, 1, EliteDrumsAction.EliteSizzleHiHat);
            Queue(engine, 1, EliteDrumsAction.EliteStomp);
        }
        else
        {
            Queue(engine, 1, EliteDrumsAction.EliteStomp);
            Queue(engine, 1, EliteDrumsAction.EliteSizzleHiHat);
        }
        engine.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(pedal.WasHit && hat.WasHit, Is.True);
            Assert.That(engine.EngineStats.OptionalPedalHits, Is.EqualTo(1));
            Assert.That(engine.EngineStats.AssistedPedalNotes, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(100));
        });
    }

    [Test]
    public void EarlyExactLateInputPriority()
    {
        var early = Note(EliteDrumNote.EliteDrumPad.HatPedal, 1);
        var exact = Note(EliteDrumNote.EliteDrumPad.HatPedal, 2);
        var late = Note(EliteDrumNote.EliteDrumPad.HatPedal, 3);
        var engine = Create(true, early, exact, late);
        Queue(engine, 0.95, EliteDrumsAction.EliteStomp);
        engine.Update(0.95);
        Queue(engine, 2, EliteDrumsAction.EliteStomp);
        engine.Update(2);
        Queue(engine, 3.01, EliteDrumsAction.EliteStomp);
        engine.Update(3.01);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.OptionalPedalHits, Is.EqualTo(2));
            Assert.That(engine.EngineStats.AssistedPedalNotes, Is.EqualTo(1));
            Assert.That(late.WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.Zero);
        });
    }

    [Test]
    public void PriorHandRetainsItsHitWindowBeforeLaterAutoPedal()
    {
        var hand = Note(EliteDrumNote.EliteDrumPad.Snare, 0.99);
        var pedal = Note(EliteDrumNote.EliteDrumPad.HatPedal, 1);
        var engine = Create(true, hand, pedal);
        engine.Update(1);
        Assert.That(hand.WasHit || hand.WasMissed, Is.False);
        Assert.That(pedal.WasHit, Is.False);
        Queue(engine, 1.01, EliteDrumsAction.EliteSnare);
        engine.Update(1.01);
        Assert.That(hand.WasHit, Is.True);
        Assert.That(pedal.WasHit, Is.True);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Assert.That(engine.EngineStats.AssistedPedalNotes, Is.EqualTo(1));
    }

    [Test]
    public void SameChordRequiredHandMayStillBeHitAfterPedalTime()
    {
        var pedal = Note(EliteDrumNote.EliteDrumPad.HatPedal);
        var hat = Note(EliteDrumNote.EliteDrumPad.HiHat);
        pedal.AddChildNote(hat);
        var engine = Create(true, pedal);
        engine.Update(1);
        Assert.That(pedal.WasHit, Is.False);
        Queue(engine, 1.01, EliteDrumsAction.EliteSizzleHiHat);
        engine.Update(1.01);
        Assert.That(hat.WasHit, Is.True);
        Assert.That(pedal.WasHit, Is.True);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Assert.That(engine.EngineStats.AssistedPedalNotes, Is.EqualTo(1));
    }

    [Test]
    public void SeekBotReplayDeterminism()
    {
        var pedal = Note(EliteDrumNote.EliteDrumPad.HatPedal);
        var engine = Create(true, pedal);
        engine.Update(1);
        engine.Reset();
        pedal.ResetNoteState();
        engine.Update(1);
        Assert.That(engine.EngineStats.AssistedPedalNotes, Is.EqualTo(1));
        Assert.That(engine.EngineStats.CommittedScore, Is.Zero);
    }
}
