using System;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Engine.Drums;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteFillPolicyV1Tests
{
    private static EliteFillPolicyParameters Parameters => new(
        entryWindowSeconds: 0.050,
        graceWindowSeconds: 0.050,
        pendingCommitDelaySeconds: 0.025,
        groupingToleranceSeconds: 0.001,
        barrierLatchSeconds: 0.250,
        barrierRecoverySeconds: 0.500,
        cadenceStepSeconds: 0.100);

    [Test]
    public void Deadline_UsesFixedEntryAndGraceBoundaries()
    {
        var deadline = EliteFillDeadlineCalculator.Calculate(10.000, Parameters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deadline.EntryUntil, Is.EqualTo(10.050).Within(1e-12));
            Assert.That(deadline.GraceUntil, Is.EqualTo(10.100).Within(1e-12));
            Assert.That(deadline.CommitAt, Is.EqualTo(10.125).Within(1e-12));
            Assert.That(deadline.Classify(10.050), Is.EqualTo(EliteFillWindow.Entry));
            Assert.That(deadline.Classify(10.051), Is.EqualTo(EliteFillWindow.Grace));
            Assert.That(deadline.Classify(10.100), Is.EqualTo(EliteFillWindow.Grace));
            Assert.That(deadline.Classify(10.101), Is.EqualTo(EliteFillWindow.Outside));
        }
    }

    [Test]
    public void Cadence_ProducesGYBAndGYYBAtDeterministicTimes()
    {
        var gyb = EliteFillCadenceCalculator.Calculate(EliteCadencePattern.GYB, 2.000, 0.100);
        var gyyb = EliteFillCadenceCalculator.Calculate(EliteCadencePattern.GYYB, 2.000, 0.100);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gyb.Select(point => point.Symbol), Is.EqualTo(new[]
            {
                EliteCadenceSymbol.Green, EliteCadenceSymbol.Yellow, EliteCadenceSymbol.Blue,
            }));
            Assert.That(gyb.Select(point => point.Timestamp), Is.EqualTo(new[] { 2.0, 2.1, 2.2 }));
            Assert.That(gyyb.Select(point => point.Symbol), Is.EqualTo(new[]
            {
                EliteCadenceSymbol.Green, EliteCadenceSymbol.Yellow,
                EliteCadenceSymbol.Yellow, EliteCadenceSymbol.Blue,
            }));
            Assert.That(gyyb.Select(point => point.Timestamp), Is.EqualTo(new[] { 2.0, 2.1, 2.2, 2.3 }));
        }
    }

    [Test]
    public void Barrier_IsLatchedThenRecoversAtFixedTimes()
    {
        var barrier = EliteFillBarrierCalculator.Calculate(4.000, Parameters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(barrier.At(4.000), Is.EqualTo(EliteFillBarrierPhase.Latched));
            Assert.That(barrier.At(4.249), Is.EqualTo(EliteFillBarrierPhase.Latched));
            Assert.That(barrier.At(4.250), Is.EqualTo(EliteFillBarrierPhase.Recovering));
            Assert.That(barrier.At(4.749), Is.EqualTo(EliteFillBarrierPhase.Recovering));
            Assert.That(barrier.At(4.750), Is.EqualTo(EliteFillBarrierPhase.Clear));
        }
    }

    [Test]
    public void Grouping_UsesToleranceAndStableIdTieBreak()
    {
        var groups = EliteFillTimeGrouping.Group(new[]
        {
            new EliteFillCandidate(3, 1.001, EliteCadenceSymbol.Blue),
            new EliteFillCandidate(1, 1.000, EliteCadenceSymbol.Green),
            new EliteFillCandidate(2, 1.0005, EliteCadenceSymbol.Yellow),
            new EliteFillCandidate(4, 1.003, EliteCadenceSymbol.Blue),
        }, 0.001);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(groups[0].Members.Select(candidate => candidate.Id), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(groups[1].Members.Select(candidate => candidate.Id), Is.EqualTo(new[] { 4 }));
            Assert.That(EliteFillTimeGrouping.AreEqual(1.000, 1.001, 0.001), Is.True);
            Assert.That(EliteFillTimeGrouping.AreEqual(1.000, 1.0011, 0.001), Is.False);
        }
    }

    [Test]
    public void Validation_RejectsInvalidPolicyAndTimestampValues()
    {
        var result = new EliteFillPolicyParameters(entryWindowSeconds: double.NaN).Validate();

        Assert.That(result.IsValid, Is.False);
        Assert.That(() => new EliteFillPolicyV1(new EliteFillPolicyParameters(entryWindowSeconds: -1)),
            Throws.ArgumentException);
        Assert.That(() => EliteFillParameterValidation.RequireTimestamp(double.PositiveInfinity),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
