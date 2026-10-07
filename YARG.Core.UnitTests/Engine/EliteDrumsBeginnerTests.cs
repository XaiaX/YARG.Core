using System;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

public class EliteDrumsBeginnerTests
{
    private static EliteDrumNote Note(EliteDrumNote.EliteDrumPad pad = EliteDrumNote.EliteDrumPad.Wildcard,
        NoteFlags flags = NoteFlags.None, double time = 1, uint tick = 960,
        EliteDrumNote.EliteDrumsHatPedalType pedal = EliteDrumNote.EliteDrumsHatPedalType.Stomp) =>
        new(pad, DrumNoteType.Neutral, EliteDrumNote.EliteDrumsHatState.Indifferent, pedal,
            false, DrumNoteFlags.None, flags, EliteDrumNote.EliteDrumsChannelFlag.None, time, tick, false);

    private static EliteDrumsEngine Engine(InstrumentDifficulty<EliteDrumNote> chart, bool bot = false, bool assist = false)
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.ProFourLane);
        return new EliteDrumsEngine(chart, sync, parameters, bot, true, assist);
    }

    private static InstrumentDifficulty<EliteDrumNote> Chart(params EliteDrumNote[] notes) =>
        new(Instrument.EliteDrums, Difficulty.Beginner, new(notes), new(), new());

    [Test]
    public void EveryCompatibleActionScoresOneNeutralWildcard()
    {
        foreach (var action in Enum.GetValues<EliteDrumsAction>())
        {
            var note = Note();
            var engine = Engine(Chart(note));
            var input = action is EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteSplash
                ? GameInput.Create(1, action, true) : GameInput.Create(1, action, 0.8f);
            engine.QueueInput(ref input);
            engine.Update(1);
            Assert.That(note.WasHit, Is.True, action.ToString());
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(50), action.ToString());
            Assert.That(engine.EngineStats.DynamicsBonus, Is.Zero);
        }
    }

    [Test]
    public void ReleasesInvalidValuesAndMenuDoNotHit()
    {
        GameInput[] inputs = [GameInput.Create(1, EliteDrumsAction.EliteSnare, 0f),
            GameInput.Create(1, EliteDrumsAction.EliteStomp, false),
            GameInput.Create(1, EliteDrumsAction.EliteSnare, float.NaN),
            GameInput.Create(1, EliteDrumsAction.EliteSnare, float.PositiveInfinity),
            GameInput.Create(1, EliteDrumsAction.EliteSnare, -1f),
            new GameInput(1, 99, 1f), GameInput.Create(1, MenuAction.Start, true)];
        foreach (var inputValue in inputs)
        {
            var note = Note();
            var engine = Engine(Chart(note));
            var input = inputValue;
            engine.QueueInput(ref input);
            engine.Update(1);
            Assert.That(note.WasHit, Is.False);
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        }
    }

    [Test]
    public void BotUsesStableWildcardActionAndAssistanceDoesNotHit()
    {
        var bot = Engine(Chart(Note()), bot: true);
        EliteDrumsAction? action = null;
        bot.OnPadHit += (hitAction, hit, _, _, _, _) => { if (hit) action = hitAction; };
        bot.Update(1);
        Assert.That(action, Is.EqualTo(EliteDrumsAction.WildcardPad));
        Assert.That(bot.EngineStats.NotesHit, Is.EqualTo(1));
        var assisted = Engine(Chart(Note()), assist: true);
        assisted.Update(2);
        Assert.That(assisted.EngineStats.NotesHit, Is.Zero);
        Assert.That(assisted.EngineStats.NotesMissed, Is.EqualTo(1));
    }

    [Test]
    public void CollapseAggregatesBoundariesRemovesRollsAndIgnoresControlOnsets()
    {
        var hand = Note(EliteDrumNote.EliteDrumPad.Snare, NoteFlags.StarPower | NoteFlags.StarPowerStart |
            NoteFlags.SoloStart | NoteFlags.Tremolo | NoteFlags.LaneStart);
        hand.AddChildNote(Note(EliteDrumNote.EliteDrumPad.Kick, NoteFlags.StarPowerEnd | NoteFlags.SoloEnd |
            NoteFlags.CodaStart | NoteFlags.BigRockEnding));
        var source = Chart(hand, Note(EliteDrumNote.EliteDrumPad.Ride, NoteFlags.CodaEnd),
            Note(EliteDrumNote.EliteDrumPad.HatPedal, time: 2, tick: 1920,
                pedal: EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator));
        source.Phrases.Add(new Phrase(PhraseType.EliteDrums_KickLane, 1, 1, 960, 960));
        source.Phrases.Add(new Phrase(PhraseType.TremoloLane, 1, 1, 960, 960));
        source.Phrases.Add(new Phrase(PhraseType.Solo, 1, 1, 960, 960));
        source.SetEliteDrumNativeAuthoredLaneRecords([new EliteDrumNativeAuthoredLaneRecord(0,
            PhraseType.EliteDrums_KickLane, 960, 1920, Array.Empty<EliteDrumSourceDefinition>())]);
        var collapsed = EliteDrumsBeginner.Collapse(source);
        Assert.That(collapsed.Notes, Has.Count.EqualTo(1));
        var note = collapsed.Notes[0];
        Assert.That(note.ChildNotes, Is.Empty);
        Assert.That(note.Pad, Is.EqualTo((int) EliteDrumNote.EliteDrumPad.Wildcard));
        Assert.That(note.Flags, Is.EqualTo(NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd |
            NoteFlags.SoloStart | NoteFlags.SoloEnd | NoteFlags.CodaStart | NoteFlags.CodaEnd | NoteFlags.BigRockEnding));
        Assert.That(collapsed.Phrases, Has.Count.EqualTo(1));
        Assert.That(collapsed.EliteDrumNativeAuthoredLaneRecords, Is.Empty);
        Assert.That(collapsed.EliteDrumAuthoredLanePhraseRecords, Is.Empty);
        Assert.That(collapsed.EliteDrumVisualDescriptors, Is.Empty);
        Assert.That(source.Notes, Has.Count.EqualTo(3));
        var playableSource = Chart(Note(EliteDrumNote.EliteDrumPad.Snare),
            Note(EliteDrumNote.EliteDrumPad.HatPedal, time: 2, tick: 1920,
                pedal: EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator),
            Note(EliteDrumNote.EliteDrumPad.Snare, time: 3, tick: 2880));
        playableSource.Phrases.Add(new Phrase(PhraseType.EliteDrums_SnareLane, 1, 2, 960, 1920));
        playableSource.SetEliteDrumNativeAuthoredLaneRecords([new EliteDrumNativeAuthoredLaneRecord(0,
            PhraseType.EliteDrums_SnareLane, 960, 2880, Array.Empty<EliteDrumSourceDefinition>())]);
        var playable = EliteDrumsBeginner.Collapse(playableSource);
        Assert.That(playable.Notes, Has.Count.EqualTo(2));
        var engine = Engine(playable);
        int misses = 0;
        engine.OnNoteMissed += (_, _) => misses++;
        var strike = GameInput.Create(1, EliteDrumsAction.EliteSnare, 0.8f);
        engine.QueueInput(ref strike);
        engine.Update(1);
        var extra = GameInput.Create(1.5, EliteDrumsAction.EliteSnare, 0.8f);
        engine.QueueInput(ref extra);
        engine.Update(1.5);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Assert.That(misses, Is.Zero);
        var finalStrike = GameInput.Create(3, EliteDrumsAction.Kick, 0.8f);
        engine.QueueInput(ref finalStrike);
        engine.Update(3);
        engine.Update(4);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.TotalNotes, Is.EqualTo(2));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.NotesMissed, Is.Zero);
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
            Assert.That(misses, Is.Zero);
            Assert.That(playable.Notes.TrueForAll(n => n.WasHit && !n.WasMissed), Is.True);
        });
    }
}
