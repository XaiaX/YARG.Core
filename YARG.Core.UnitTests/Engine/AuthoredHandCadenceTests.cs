using System;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Engine.Drums;

namespace YARG.Core.UnitTests.Engine;

public sealed class AuthoredHandCadenceTests
{
    private static AuthoredHandCadence.Lane Lane(int pad, double start, double end, double step) =>
        new(pad, Enumerable.Range(0, (int)Math.Round((end - start) / step) + 1)
            .Select(i => start + i * step).ToArray());

    [Test]
    public void AbandonedPadExpiresWithoutBreakingOtherPadsAndCanRecover()
    {
        var cadence = new AuthoredHandCadence(new[] { Lane(0, 0, 3, .3),
            Lane(1, .1, 3.1, .3), Lane(2, .2, 3.2, .3) });
        cadence.Input(0, .3, .2);
        for (int i = 0; i < 16; i++) cadence.Input(1 + i % 2, .4 + i * .1, .2);
        Assert.That(cadence.Covers(0, 1.8), Is.False);
        Assert.That(cadence.Covers(1, 1.8), Is.True);
        Assert.That(cadence.Covers(2, 1.8), Is.True);
        cadence.Input(0, 1.95, .2);
        Assert.That(cadence.Covers(0, 2), Is.True);
    }

    [TestCase(.1)]
    [TestCase(.2)]
    public void JoiningAndRetiringLanesDoNotResetContinuingCadence(double newLaneStep)
    {
        var cadence = new AuthoredHandCadence(new[] { Lane(0, 0, 2, .2),
            Lane(1, 1, 3, newLaneStep), Lane(2, 2, 4, .2) });
        cadence.Input(0, .9, .2);
        cadence.Input(1, 1, .2);
        Assert.That(cadence.Covers(0, 1.1), Is.True);
        cadence.Input(0, 1.95, .2);
        cadence.Input(2, 2, .2);
        cadence.Input(1, 2.1, .2);
        Assert.That(cadence.Covers(1, 2.2), Is.True);
        Assert.That(cadence.Covers(2, 2.2), Is.True);
        cadence.Input(0, 2.9, .2);
        Assert.That(cadence.Covers(1, 3), Is.False, "A retired pad cannot refresh the surviving group.");
    }

    [Test]
    public void RepeatedPadDoesNotRefreshAlternationAndResetClearsAllCoverage()
    {
        var cadence = new AuthoredHandCadence(new[] { Lane(0, 0, 3, .2), Lane(1, 0, 3, .2) });
        cadence.Input(0, 1, .2);
        cadence.Input(1, 1.1, .2);
        cadence.Input(1, 1.2, .2);
        cadence.Input(1, 1.3, .2);
        Assert.That(cadence.Covers(0, 1.4), Is.False);
        cadence.Input(0, 1.4, .2);
        Assert.That(cadence.Covers(1, 1.5), Is.True);
        cadence.Reset();
        Assert.That(cadence.Covers(1, 1.5), Is.False);
    }

    [Test]
    public void AbacAllowsLessFrequentPadsWithoutInferringHands()
    {
        var cadence = new AuthoredHandCadence(new[] { Lane(0, 0, 4, .2),
            Lane(1, .1, 4.1, .4), Lane(2, .3, 4.3, .4) });
        cadence.Input(1, 1.3, .2);
        cadence.Input(2, 1.5, .2);
        cadence.Input(0, 1.7, .2);
        Assert.That(cadence.Covers(1, 1.8), Is.True, "B's local recurrence is twice A's.");
    }
    [Test]
    public void ParticipationTracksLocalRecurrenceInsteadOfWholePhraseAverage()
    {
        var lanes = Enumerable.Range(0, 3).Select(pad =>
        {
            var early = Enumerable.Range(0, 7).Select(i => pad * .1 + i * .3);
            var late = Enumerable.Range(0, pad == 0 ? 16 : 8)
                .Select(i => 3.0 + pad * .1 + i * (pad == 0 ? .2 : .4));
            return new AuthoredHandCadence.Lane(pad, early.Concat(late).ToArray());
        }).ToArray();
        var earlyCadence = new AuthoredHandCadence(lanes);
        earlyCadence.Input(1, .4, .2);
        earlyCadence.Input(0, 1.04, .2);
        Assert.That(earlyCadence.Covers(1, 1.05), Is.False);
        var lateCadence = new AuthoredHandCadence(lanes);
        lateCadence.Input(1, 3.9, .2);
        lateCadence.Input(0, 4.54, .2);
        Assert.That(lateCadence.Covers(1, 4.55), Is.True,
            "B recurs half as often as A in the local ABAC section.");
    }

}
