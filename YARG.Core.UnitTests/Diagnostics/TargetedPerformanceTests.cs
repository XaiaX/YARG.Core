// pattern: Imperative Shell
using System.Threading;
using YARG.Core.Diagnostics;
using YARG.Core.Chart;
using YARG.Core.Input;
using YARG.Core.Game;
using YARG.Core.Engine.Drums;
using NUnit.Framework;
using YARG.Core.Engine.Drums.Engines;

namespace YARG.Core.UnitTests.Diagnostics;

public sealed class TargetedPerformanceTests
{
    [Test]
    public void TargetedCollectionIsOptInAndConsumesEachIntervalOnce()
    {
        var type = typeof(EliteDrumsEngine).Assembly.GetType("YARG.Core.Diagnostics.TargetedPerformance");
        Assert.That(type, Is.Not.Null, "Targeted diagnostics must be available without a Unity dependency.");
        var configure = type!.GetMethod("SetEnabled")!;
        var capture = type.GetMethod("TakeSnapshot")!;
        var metric = type.Assembly.GetType("YARG.Core.Diagnostics.TargetedOperation")!;
        var measure = type.GetMethod("Measure")!;
        long Calls(object snapshot) => (long)snapshot.GetType().GetField("RollSchedule")!.FieldType
            .GetField("Calls")!.GetValue(snapshot.GetType().GetField("RollSchedule")!.GetValue(snapshot))!;
        void Measure() => ((IDisposable)measure.Invoke(null, new[] { Enum.Parse(metric, "RollSchedule") })!).Dispose();
        try
        {
            configure.Invoke(null, new object[] { false });
            Measure();
            Assert.That(Calls(capture.Invoke(null, null)!), Is.Zero);
            configure.Invoke(null, new object[] { true });
            Measure(); Measure();
            Assert.That(Calls(capture.Invoke(null, null)!), Is.EqualTo(2));
            Assert.That(Calls(capture.Invoke(null, null)!), Is.Zero, "Intervals must not accumulate across frames.");
        }
        finally { configure.Invoke(null, new object[] { false }); }
    }
}

public sealed class TargetedPerformanceLifecycleTests
{
    [TearDown]
    public void Reset() => TargetedPerformance.SetEnabled(false);

    [TestCase("GameplayAwake")]
    [TestCase("GameplayManagerDiscovery")]
    [TestCase("NoteSpawn")]
    [TestCase("ElementEnable")]
    [TestCase("ElementSetActive")]
    [TestCase("ElementInitialize")]
    [TestCase("ElementFirstVisualUpdate")]
    [TestCase("NoteGroupInitialize")]
    [TestCase("MaterialAccess")]
    [TestCase("NoteGroupSetActive")]
    [TestCase("PedalMeshBuild")]
    [TestCase("GameManagerUpdate")]
    [TestCase("SongRunnerUpdate")]
    [TestCase("AudioSynchronize")]
    [TestCase("AudioClockQuery")]
    [TestCase("AudioClockCorrection")]
    [TestCase("AudioClockMixerLock")]
    [TestCase("AudioClockNativeQuery")]
    [TestCase("AudioClockBassConvert")]
    [TestCase("GameplaySpeedUpdate")]
    [TestCase("GameplayBeatEvents")]
    [TestCase("GameplayCrowdEvents")]
    [TestCase("GameplayPlayerUpdate")]
    [TestCase("GameplayScoreUpdate")]
    [TestCase("GameplaySongEnd")]
    public void OperationIsOptInAndDrainsIndependently(string name)
    {
        Assert.That(Enum.TryParse<TargetedOperation>(name, out var operation), Is.True,
            "Diagnostic phases must be measurable independently.");
        var field = typeof(TargetedPerformanceSnapshot).GetField(name);
        Assert.That(field, Is.Not.Null);
        using (TargetedPerformance.Measure(operation)) { }
        Assert.That(((TargetedTiming)field!.GetValue(TargetedPerformance.TakeSnapshot())!).Calls, Is.Zero);
        TargetedPerformance.SetEnabled(true);
        using (TargetedPerformance.Measure(operation)) { }
        var snapshot = TargetedPerformance.TakeSnapshot();
        Assert.That(((TargetedTiming)field.GetValue(snapshot)!).Calls, Is.EqualTo(1));
        Assert.That(snapshot.RollSchedule.Calls, Is.Zero);
        Assert.That(((TargetedTiming)field.GetValue(TargetedPerformance.TakeSnapshot())!).Calls, Is.Zero);
    }

    [Test]
    public void OldScopeCannotWriteIntoANewCollectionSession()
    {
        TargetedPerformance.SetEnabled(true);
        var old = TargetedPerformance.Measure(TargetedOperation.HiHatDiscovery);
        long before = TargetedPerformance.TakeSnapshot().Generation;
        TargetedPerformance.SetEnabled(false);
        TargetedPerformance.SetEnabled(true);
        old.Dispose();
        var snapshot = TargetedPerformance.TakeSnapshot();
        Assert.That(snapshot.Generation, Is.GreaterThan(before));
        Assert.That(snapshot.HiHatDiscovery.Calls, Is.Zero);
        using (TargetedPerformance.Measure(TargetedOperation.HiHatDiscovery)) { }
        Assert.That(TargetedPerformance.TakeSnapshot().HiHatDiscovery.Calls, Is.EqualTo(1));
    }

    [Test]
    public void ContextAndFirstUseCountsDoNotLeakBetweenIntervalsOrToggleSessions()
    {
        TargetedPerformance.SetEnabled(true);
        using (TargetedPerformance.MeasureWithContext(TargetedOperation.NoteSpawn, 42)) { }
        TargetedPerformance.Current!.RecordFirstElementEnable();
        TargetedPerformance.Current.RecordFirstNoteGroupInitialization();
        var snapshot = TargetedPerformance.TakeSnapshot();
        Assert.That(snapshot.NoteSpawn.MaxContext, Is.EqualTo(42));
        Assert.That(snapshot.NoteSpawn.MaxStartTicks, Is.GreaterThan(0));
        Assert.That(snapshot.FirstElementEnables, Is.EqualTo(1));
        Assert.That(snapshot.FirstNoteGroupInitializations, Is.EqualTo(1));
        var old = TargetedPerformance.MeasureWithContext(TargetedOperation.NoteSpawn, 99);
        TargetedPerformance.SetEnabled(false);
        TargetedPerformance.SetEnabled(true);
        old.Dispose();
        snapshot = TargetedPerformance.TakeSnapshot();
        Assert.That(snapshot.NoteSpawn.Calls, Is.Zero);
        Assert.That(snapshot.NoteSpawn.MaxContext, Is.Zero);
        Assert.That(snapshot.FirstElementEnables, Is.Zero);
        Assert.That(snapshot.FirstNoteGroupInitializations, Is.Zero);
    }

    [Test]
    public void BackgroundAnalysisDoesNotContributeToGameplayCollection()
    {
        TargetedPerformance.SetEnabled(true);
        bool backgroundEnabled = true;
        var thread = new Thread(() =>
        {
            backgroundEnabled = TargetedPerformance.Enabled;
            using (TargetedPerformance.Measure(TargetedOperation.RollResolve)) { }
        });
        thread.Start();
        Assert.That(thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(backgroundEnabled, Is.False);
        Assert.That(TargetedPerformance.TakeSnapshot().RollResolve.Calls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MeasurementAndSnapshotDoNotAllocatePerCall(bool enabled)
    {
        TargetedPerformance.SetEnabled(enabled);
        var operations = Enum.GetValues<TargetedOperation>();
        for (int i = 0; i < 100; i++)
        {
            foreach (var operation in operations)
                using (TargetedPerformance.MeasureWithContext(operation, i)) { }
            TargetedPerformance.Current?.RecordFirstElementEnable();
            TargetedPerformance.TakeSnapshot();
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            foreach (var operation in operations)
                using (TargetedPerformance.MeasureWithContext(operation, i)) { }
            TargetedPerformance.Current?.RecordFirstElementEnable();
            TargetedPerformance.TakeSnapshot();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }
}

public sealed class TargetedPerformanceEngineTests
{
    [TearDown]
    public void Reset() => TargetedPerformance.SetEnabled(false);

    [Test]
    public void CountersObserveRealRollWorkWithoutChangingScoring()
    {
        var disabled = Run(false);
        var enabled = Run(true);
        Assert.Multiple(() =>
        {
            Assert.That(enabled.score, Is.EqualTo(100));
            Assert.That(enabled.score, Is.EqualTo(disabled.score));
            Assert.That(enabled.hits, Is.EqualTo(disabled.hits));
            Assert.That(enabled.hits, Is.EqualTo(2));
            Assert.That(disabled.snapshot.RollSchedule.Calls, Is.Zero);
            Assert.That(enabled.snapshot.RollSchedule.Calls, Is.GreaterThan(0));
            Assert.That(enabled.snapshot.RollResolve.Calls, Is.GreaterThan(0));
            Assert.That(enabled.snapshot.LaneMapBuild.Calls, Is.EqualTo(1));
            Assert.That(enabled.snapshot.ScheduleNotesExamined, Is.GreaterThan(0));
            Assert.That(enabled.snapshot.ResolveNotesExamined, Is.GreaterThan(0));
            Assert.That(enabled.snapshot.LaneLookups, Is.EqualTo(enabled.snapshot.LaneMembers),
                "The load-time index should exclude nonmembers from cadence lookups.");
            Assert.That(enabled.snapshot.LaneMembers, Is.GreaterThan(0));
            Assert.That(enabled.snapshot.DeadlinesQueued, Is.GreaterThan(0));
            Assert.That(enabled.snapshot.Continuations, Is.EqualTo(1));
        });
    }

    [Test]
    public void ChartWithoutAuthoredRollsDoesNotRunRollCandidateScans()
    {
        var result = Run(true, includeLane: false);
        Assert.That(result.hits, Is.EqualTo(1));
        Assert.That(result.score, Is.EqualTo(50));
        Assert.That(result.snapshot.ScheduleNotesExamined, Is.Zero);
        Assert.That(result.snapshot.ResolveNotesExamined, Is.Zero);
    }

    [Test]
    public void UnrelatedChartNotesDoNotIncreaseRollCandidateScanWork()
    {
        var small = Run(true);
        var large = Run(true, extraNotes: 1000);
        Assert.That(large.hits, Is.EqualTo(small.hits));
        Assert.That(large.score, Is.EqualTo(small.score));
        Assert.That(large.snapshot.Continuations, Is.EqualTo(small.snapshot.Continuations));
        Assert.That(large.snapshot.ScheduleNotesExamined, Is.EqualTo(small.snapshot.ScheduleNotesExamined));
        Assert.That(large.snapshot.ResolveNotesExamined, Is.EqualTo(small.snapshot.ResolveNotesExamined));
    }

    [Test]
    public void DistantFutureRollMembersDoNotIncreaseSchedulingWork()
    {
        var small = Run(true);
        var large = Run(true, extraNotes: 1000, futureRoll: true);
        Assert.That(large.hits, Is.EqualTo(small.hits));
        Assert.That(large.score, Is.EqualTo(small.score));
        Assert.That(large.snapshot.Continuations, Is.EqualTo(small.snapshot.Continuations));
        Assert.That(large.snapshot.ScheduleNotesExamined, Is.EqualTo(small.snapshot.ScheduleNotesExamined));
        Assert.That(large.snapshot.ResolveNotesExamined, Is.EqualTo(small.snapshot.ResolveNotesExamined));
    }

    [Test]
    public void ResolvedRollMembersAreNotRevisitedDuringLaterFrames()
    {
        var result = Run(true, inspectIdle: true);
        Assert.That(result.hits, Is.EqualTo(2));
        Assert.That(result.score, Is.EqualTo(100));
        Assert.That(result.snapshot.ScheduleNotesExamined, Is.Zero);
        Assert.That(result.snapshot.ResolveNotesExamined, Is.Zero);
    }

    [Test]
    public void ResetReusesMembershipButResetsLiveCadenceAndNoteState()
    {
        var original = Run(true);
        var reset = Run(true, replayAfterReset: true);
        Assert.That(reset.hits, Is.EqualTo(original.hits));
        Assert.That(reset.score, Is.EqualTo(original.score));
        Assert.That(reset.snapshot.Continuations, Is.EqualTo(original.snapshot.Continuations));
        Assert.That(reset.snapshot.LaneMapBuild.Calls, Is.Zero);
        Assert.That(reset.snapshot.ScheduleNotesExamined, Is.EqualTo(original.snapshot.ScheduleNotesExamined));
    }

    private static (int score, int hits, TargetedPerformanceSnapshot snapshot) Run(bool enabled,
        bool includeLane = true, int extraNotes = 0, bool replayAfterReset = false,
        bool futureRoll = false, bool inspectIdle = false)
    {
        TargetedPerformance.SetEnabled(enabled);
        TargetedPerformance.TakeSnapshot();
        var notes = new List<EliteDrumNote>();
        var sources = new List<EliteDrumSourceDefinition>();
        foreach (double time in new[] { 1.0, 1.04, 1.5, 3.0 }.Concat(Enumerable.Range(0, extraNotes).Select(i => 4.0 + i)))
        {
            uint tick = (uint)(time * 960);
            var source = new EliteDrumSourceDefinition($"diag-{notes.Count}", notes.Count,
                (int)EliteDrumNote.EliteDrumPad.Kick, tick, 0, time);
            sources.Add(source);
            notes.Add(new EliteDrumNote(EliteDrumNote.EliteDrumPad.Kick,
                DrumNoteType.Neutral, EliteDrumNote.EliteDrumsHatState.Indifferent,
                EliteDrumNote.EliteDrumsHatPedalType.Stomp, false,
                DrumNoteFlags.None, NoteFlags.None,
                EliteDrumNote.EliteDrumsChannelFlag.None, time, tick, false, source));
        }
        var chart = new InstrumentDifficulty<EliteDrumNote>(
            YARG.Core.Instrument.EliteDrums, YARG.Core.Difficulty.Expert, new(notes), new(), new());
        if (includeLane) chart.SetEliteDrumNativeAuthoredLaneRecords(new[]
        {
            new EliteDrumNativeAuthoredLaneRecord(0, PhraseType.EliteDrums_KickLane,
                960, 2000, sources.Take(3).ToArray())
        });
        if (futureRoll) chart.SetEliteDrumNativeAuthoredLaneRecords(
            chart.EliteDrumNativeAuthoredLaneRecords.Concat(new[]
            {
                new EliteDrumNativeAuthoredLaneRecord(1, PhraseType.EliteDrums_KickLane,
                    notes[4].Tick, notes[^1].Tick, sources.Skip(4).ToArray())
            }).ToArray());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var parameters = EnginePreset.Default.Drums.Copy().Create(
            new[] {1f, 2f, 3f, 4f, 5f, 6f}, new[] {1f, 2f, 3f, 4f, 5f, 6f},
            DrumsEngineParameters.DrumMode.ProFourLane);
        var engine = new EliteDrumsEngine(chart, sync, parameters, false, true);
        var input = GameInput.Create(1, EliteDrumsAction.Kick, 0.8f);
        engine.QueueInput(ref input);
        engine.Update(1);
        engine.Update(2);
        if (replayAfterReset)
        {
            engine.Reset();
            TargetedPerformance.TakeSnapshot();
            engine.QueueInput(ref input);
            engine.Update(1);
            engine.Update(2);
        }
        if (inspectIdle)
        {
            // Allow one pass to retire the last resolved member, then measure idle work.
            engine.Update(2.1);
            TargetedPerformance.TakeSnapshot();
            for (int i = 1; i <= 60; i++) engine.Update(2.1 + i / 100.0);
        }
        return (engine.EngineStats.CommittedScore, engine.EngineStats.NotesHit,
            TargetedPerformance.TakeSnapshot());
    }
}
