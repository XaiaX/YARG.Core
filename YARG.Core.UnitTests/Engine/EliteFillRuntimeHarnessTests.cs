using System.Linq;
using NUnit.Framework;
using YARG.Core.Engine.Drums;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteFillRuntimeHarnessTests
{
    private static EliteFillPolicyV1 Policy => new(new EliteFillPolicyParameters(
        entryWindowSeconds: 0.050,
        graceWindowSeconds: 0.050,
        pendingCommitDelaySeconds: 0.025,
        groupingToleranceSeconds: 0.001,
        barrierLatchSeconds: 0.250,
        barrierRecoverySeconds: 0.500,
        cadenceStepSeconds: 0.100));

    [Test]
    public void Trace_DistinguishesPendingFromLaterCommit()
    {
        var harness = new EliteFillRuntimeHarness(Policy, 10.000);
        var pending = harness.Submit(10.000);
        var beforeCommit = harness.Advance(10.124);
        var committed = harness.Advance(10.125);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pending.PendingCount, Is.EqualTo(1));
            Assert.That(pending.CommittedCount, Is.Zero);
            Assert.That(beforeCommit.PendingCount, Is.EqualTo(1));
            Assert.That(beforeCommit.CommittedCount, Is.Zero);
            Assert.That(committed.PendingCount, Is.Zero);
            Assert.That(committed.CommittedCount, Is.EqualTo(1));
            Assert.That(harness.Trace.Select(trace => trace.Kind), Is.EqualTo(new[]
            {
                EliteFillRuntimeEventKind.Pending,
                EliteFillRuntimeEventKind.Commit,
            }));
            Assert.That(harness.Trace[0].Timestamp, Is.EqualTo(10.000).Within(1e-12));
            Assert.That(harness.Trace[1].Timestamp, Is.EqualTo(10.125).Within(1e-12));
        }
    }

    [Test]
    public void Trace_RecordsBarrierLatchAndRecoveryWithoutMutatingSnapshots()
    {
        var harness = new EliteFillRuntimeHarness(Policy, 1.000);
        var latched = harness.LatchBarrier(1.000);
        var recovering = harness.Advance(1.250);
        var clear = harness.Advance(1.750);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(latched.BarrierLatched, Is.True);
            Assert.That(latched.BarrierGeneration, Is.EqualTo(1));
            Assert.That(recovering.BarrierPhase, Is.EqualTo(EliteFillBarrierPhase.Recovering));
            Assert.That(clear.BarrierLatched, Is.False);
            Assert.That(clear.BarrierPhase, Is.EqualTo(EliteFillBarrierPhase.Clear));
            Assert.That(latched.BarrierLatched, Is.True, "state snapshots are immutable");
            Assert.That(harness.Trace.Select(trace => trace.Kind), Is.EqualTo(new[]
            {
                EliteFillRuntimeEventKind.BarrierLatched,
                EliteFillRuntimeEventKind.BarrierRecovered,
            }));
        }
    }

    [Test]
    public void SubmitInEntryAndGraceRetainsPhaseUntilCommit()
    {
        var harness = new EliteFillRuntimeHarness(Policy, 5.000);

        var entry = harness.Submit(5.000);
        var grace = harness.Submit(5.075);
        var committed = harness.Advance(5.200);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Phase, Is.EqualTo(EliteFillPhase.Entry));
            Assert.That(grace.Phase, Is.EqualTo(EliteFillPhase.Grace));
            Assert.That(committed.Phase, Is.EqualTo(EliteFillPhase.Committed));
            Assert.That(committed.CommittedCount, Is.EqualTo(2));
        }
    }
}
