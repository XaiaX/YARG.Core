using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

public sealed class NativeEliteAuthoredLaneTests
{
    private static (EliteDrumNote note, EliteDrumSourceDefinition source) Member(
        EliteDrumNote.EliteDrumPad pad, double time, int ordinal)
    {
        uint tick = (uint) (time * 960);
        var source = new EliteDrumSourceDefinition($"native-lane-{ordinal}", ordinal, (int) pad, tick, 0, time);
        return (new EliteDrumNote(pad, DrumNoteType.Neutral,
            EliteDrumNote.EliteDrumsHatState.Indifferent,
            EliteDrumNote.EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None,
            NoteFlags.None, EliteDrumNote.EliteDrumsChannelFlag.None, time, tick, false, source), source);
    }

    private static EliteDrumsEngine Engine(InstrumentDifficulty<EliteDrumNote> chart, bool auto = false,
        double? laneAutohitWindow = null)
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var preset = EnginePreset.Default.Drums.Copy();
        if (laneAutohitWindow is { } window) preset.HitWindow.LaneAutohitWindow = window;
        var parameters = preset.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.ProFourLane);
        return new EliteDrumsEngine(chart, sync, parameters, false, true, auto);
    }

    private static void Strike(EliteDrumsEngine engine, double time, EliteDrumsAction action)
    {
        var input = GameInput.Create(time, action, 0.8f);
        engine.QueueInput(ref input);
        engine.Update(time);
    }

    [Test]
    public void SparseFrameResolvesOnlyAtScheduledCadence()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.04, 1);
        var third = Member(EliteDrumNote.EliteDrumPad.Snare, 1.5, 2);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note, third.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source, third.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        engine.Update(2);
        Assert.Multiple(() =>
        {
            Assert.That(first.note.WasHit, Is.True);
            Assert.That(second.note.WasHit, Is.True, "Sparse frames must replay the bounded cadence checkpoint.");
            Assert.That(third.note.WasMissed, Is.True, "A distant member must not continue indefinitely.");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(100));
        });
    }

    [Test]
    public void EntryContinuationAndExpiration()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.04, 1);
        var third = Member(EliteDrumNote.EliteDrumPad.Snare, 1.5, 2);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note, third.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source, third.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        engine.Update(1.04);
        Assert.That(second.note.WasMissed, Is.False, "Member must survive until cadence.");
        engine.Update(1.1);
        Assert.That(second.note.WasMissed, Is.False, "Member must survive the lane grace boundary.");
        engine.Update(1.2);
        Assert.Multiple(() =>
        {
            Assert.That(second.note.WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(100));
            Assert.That(third.note.WasHit || third.note.WasMissed, Is.False);
        });
        engine.Update(2);
        Assert.That(third.note.WasMissed, Is.True);
    }

    [Test]
    public void ConfiguredLaneAutohitWindowControlsNativeContinuation()
    {
        foreach (double window in new[] { 0.16, 0.06, 0.0 })
        {
            var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
            var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.12, 1);
            var third = Member(EliteDrumNote.EliteDrumPad.Snare, 1.5, 2);
            var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
                new([first.note, second.note, third.note]), new(), new());
            chart.SetEliteDrumNativeAuthoredLaneRecords([
                new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                    960, 2000, [first.source, second.source, third.source])
            ]);
            var engine = Engine(chart, laneAutohitWindow: window);
            Strike(engine, 1, EliteDrumsAction.EliteSnare);
            engine.Update(2);
            Assert.Multiple(() =>
            {
                Assert.That(second.note.WasHit, Is.EqualTo(window == 0.16), $"window={window}");
                Assert.That(second.note.WasMissed, Is.EqualTo(window != 0.16), $"window={window}");
                Assert.That(third.note.WasMissed, Is.True, "silence beyond the configured window must still miss");
            });
        }
    }

    [Test]
    public void OffPadFaultBarsPriorLaneButRealStrikeCanEnterNewLane()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.04, 1);
        var third = Member(EliteDrumNote.EliteDrumPad.Snare, 1.08, 2);
        var fourth = Member(EliteDrumNote.EliteDrumPad.Snare, 1.12, 3);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note, third.note, fourth.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source]),
            new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_SnareLane,
                960, 2000, [third.source, fourth.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        Strike(engine, 1.02, EliteDrumsAction.EliteRide);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1), "The off-pad strike must latch a real fault.");
        engine.Update(1.11);
        Assert.That(second.note.WasHit, Is.False, "Off-pad fault suppresses old-lane continuation.");
        Strike(engine, 1.12, EliteDrumsAction.EliteSnare);
        Assert.That(third.note.WasHit, Is.True, "A real strike remains eligible during barrier recovery.");
        engine.Update(2);
        Assert.That(second.note.WasMissed, Is.True);
    }

    [Test]
    public void ExactCadenceOffPadFaultDoesNotCancelDueContinuation()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.04, 1);
        var third = Member(EliteDrumNote.EliteDrumPad.Snare, 1.4, 2);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note, third.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source, third.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        Strike(engine, 1.14, EliteDrumsAction.EliteRide);
        Assert.Multiple(() =>
        {
            Assert.That(second.note.WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ExactCadenceGroupMatchesPhysicalRegardlessOfInputOrder(bool wrongFirst)
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.04, 1);
        var third = Member(EliteDrumNote.EliteDrumPad.Snare, 1.4, 2);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note, third.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source, third.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        var wrong = GameInput.Create(1.1, EliteDrumsAction.EliteRide, 0.8f);
        var matching = GameInput.Create(1.1, EliteDrumsAction.EliteSnare, 0.8f);
        if (wrongFirst) { engine.QueueInput(ref wrong); engine.QueueInput(ref matching); }
        else { engine.QueueInput(ref matching); engine.QueueInput(ref wrong); }
        engine.Update(1.1);
        Assert.Multiple(() =>
        {
            Assert.That(second.note.WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        });
    }

    [Test]
    public void MatchingPhysicalInputAtCadenceWins()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.04, 1);
        var third = Member(EliteDrumNote.EliteDrumPad.Snare, 1.4, 2);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note, third.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source, third.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        Strike(engine, 1.1, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(second.note.WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        });
    }

    [Test]
    public void AutomaticSkipDoesNotRefreshLaneFromUnrelatedStrike()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.04, 1);
        var third = Member(EliteDrumNote.EliteDrumPad.Snare, 1.19, 2);
        var ride = Member(EliteDrumNote.EliteDrumPad.Ride, 1.08, 3);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note, ride.note, third.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source, third.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        Strike(engine, 1.08, EliteDrumsAction.EliteRide);
        engine.Update(2);
        Assert.Multiple(() =>
        {
            Assert.That(ride.note.WasHit, Is.True);
            Assert.That(second.note.WasHit, Is.True);
            Assert.That(third.note.WasMissed, Is.True,
                "An automatic skip must not extend the real-input lane deadline.");
        });
    }

    [Test]
    public void ActiveLaneProtectsAllPadStrikesWithoutForgivingSilence()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.HiHat, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.HiHat, 1.4, 1);
        first.note.HatState = EliteDrumNote.EliteDrumsHatState.Closed;
        second.note.HatState = EliteDrumNote.EliteDrumsHatState.Closed;
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_HiHatLane,
                960, 2000, [first.source, second.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteClosedHiHat);
        Strike(engine, 1.2, EliteDrumsAction.EliteClosedHiHat);
        Assert.That(engine.EngineStats.Overhits, Is.Zero,
            "A matching strike inside an active lane must not break combo when continuation has expired.");
        Strike(engine, 1.21, EliteDrumsAction.EliteOpenHiHat);
        Assert.That(engine.EngineStats.Overhits, Is.Zero,
            "A strike on the hi-hat pad remains in-lane even when its articulation does not match a note.");
        Strike(engine, 1.22, EliteDrumsAction.EliteRide);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1), "An off-lane strike still breaks combo.");
        Strike(engine, 1.23, EliteDrumsAction.EliteOpenHiHat);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1),
            "An off-lane fault must not make later in-lane strikes into overhits.");
        engine.Update(2);
        Assert.That(second.note.WasMissed, Is.True, "Protected strikes must not award an unplayed note at 1.4.");
    }

    [Test]
    public void ProtectedSamePadStrikeRefreshesWithoutScoringOrBarrier()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.2, 1);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        Strike(engine, 1.05, EliteDrumsAction.EliteSnare);
        Assert.Multiple(() =>
        {
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(50));
        });
    }

    [Test]
    public void EarlyFinalHitKeepsLaneProtectedUntilChartedEnd()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var last = Member(EliteDrumNote.EliteDrumPad.Snare, 1.2, 1);
        var next = Member(EliteDrumNote.EliteDrumPad.Ride, 1.5, 2);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, last.note, next.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 1300, [first.source, last.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        Strike(engine, 1.15, EliteDrumsAction.EliteSnare);
        Assert.That(last.note.WasHit, Is.True, "The last note is struck inside its early hit window.");
        Strike(engine, 1.16, EliteDrumsAction.EliteSnare);
        Assert.That(engine.EngineStats.Overhits, Is.Zero,
            "The lane still protects its pad until the final note's charted time.");
        Strike(engine, 1.3, EliteDrumsAction.EliteSnare);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1),
            "After the lane ends, an unmatched strike is an overhit again.");
    }

    [Test]
    public void DroppedSourceFailsClosedEvenWhenTwoPlayableMembersRemain()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.04, 1);
        var missing = Member(EliteDrumNote.EliteDrumPad.Snare, 1.08, 2);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source, missing.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        engine.Update(2);
        Assert.That(second.note.WasMissed, Is.True,
            "Unresolved authored identity may not acquire a generic flag-based lane.");
    }

    [Test]
    public void OverlapAndDroppedMembership()
    {
        var first = Member(EliteDrumNote.EliteDrumPad.Snare, 1, 0);
        var second = Member(EliteDrumNote.EliteDrumPad.Snare, 1.04, 1);
        var third = Member(EliteDrumNote.EliteDrumPad.Snare, 1.08, 2);
        var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new([first.note, second.note, third.note]), new(), new());
        chart.SetEliteDrumNativeAuthoredLaneRecords([
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_SnareLane,
                960, 2000, [first.source, second.source]),
            new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_SnareLane,
                960, 2000, [second.source, third.source])
        ]);
        var engine = Engine(chart);
        Strike(engine, 1, EliteDrumsAction.EliteSnare);
        engine.Update(2);
        Assert.That(second.note.WasHit, Is.True,
            "An entered lane covers its shared member at the scheduled cadence.");
        Assert.That(third.note.WasMissed, Is.True,
            "An overlap cannot enter a second lane without a real strike on that lane.");
    }
}
