using NUnit.Framework;
using System;
using YARG.Core.Engine.Vocals.Engines;

namespace YARG.Core.UnitTests.Engine;

[TestFixture]
public sealed class PartyVocalsAssignmentTests
{
    private const double AwesomeThreshold = 0.6;
    private const double Epsilon = 1e-9;

    // AC5.1: One mic per part awards triple awesome
    [Test]
    public void BestAssignment_OneMicPerPart_AwardsTriple()
    {
        // 3 mics, 3 parts; each mic has perfect hits on its own part
        double[,] micPartHits =
        {
            { 100, 0, 0 },   // mic 0 hits part 0
            { 0, 100, 0 },   // mic 1 hits part 1
            { 0, 0, 100 }    // mic 2 hits part 2
        };
        uint[] phraseTicksTotal = { 100, 100, 100 };

        var (assignment, meters) = YargFreeVocalsEngine.ComputeBestAssignment(
            micPartHits, phraseTicksTotal, AwesomeThreshold);

        // Expect assignment: mic 0 -> part 0, mic 1 -> part 1, mic 2 -> part 2
        Assert.AreEqual(new int[] { 0, 1, 2 }, assignment);

        // Expect all meters filled to 1.0 (awesome)
        Assert.AreEqual(1.0, meters[0], Epsilon);
        Assert.AreEqual(1.0, meters[1], Epsilon);
        Assert.AreEqual(1.0, meters[2], Epsilon);
    }

    // AC5.1: One mic unison - picks one part
    [Test]
    public void BestAssignment_OneMicUnison_PicksOnePart()
    {
        // 1 mic, 2 parts; mic has perfect hits on both parts
        double[,] micPartHits =
        {
            { 100, 100 }    // mic 0 hits both parts
        };
        uint[] phraseTicksTotal = { 100, 100 };

        var (assignment, meters) = YargFreeVocalsEngine.ComputeBestAssignment(
            micPartHits, phraseTicksTotal, AwesomeThreshold);

        // Mic must be assigned to exactly one part (unison - can't fill both)
        Assert.AreEqual(1, assignment.Length);
        Assert.That(assignment[0], Is.EqualTo(0).Or.EqualTo(1));

        // Expect only one meter above threshold (awesome, not double)
        int awesomeCount = 0;
        foreach (var meter in meters)
        {
            if (meter >= AwesomeThreshold) awesomeCount++;
        }
        Assert.AreEqual(1, awesomeCount);

        // Expect the assigned meter to be filled to 1.0
        int assignedPart = assignment[0];
        Assert.AreEqual(1.0, meters[assignedPart], Epsilon);
    }

    // AC5.1: Lexicographic tiebreak
    [Test]
    public void BestAssignment_TwoMicsTied_LexicographicallyPicked()
    {
        // 2 mics, 2 parts; symmetric hit values that produce two equally-good assignments
        double[,] micPartHits =
        {
            { 100, 100 },   // mic 0 has hits on both parts
            { 100, 100 }    // mic 1 has hits on both parts
        };
        uint[] phraseTicksTotal = { 100, 100 };

        var (assignment, meters) = YargFreeVocalsEngine.ComputeBestAssignment(
            micPartHits, phraseTicksTotal, AwesomeThreshold);

        // Expect lexicographic preference: mic 0 -> part 0, mic 1 -> part 1
        Assert.AreEqual(new int[] { 0, 1 }, assignment);

        // Expect both meters filled to 1.0
        Assert.AreEqual(1.0, meters[0], Epsilon);
        Assert.AreEqual(1.0, meters[1], Epsilon);
    }

    // AC5.2: Two mics same part - meters cap at 1
    [Test]
    public void BestAssignment_TwoMicsSamePart_MetersCapAt1()
    {
        // 2 mics, 1 part; both mics have hits on the same part
        double[,] micPartHits =
        {
            { 80, 0 },    // mic 0 hits part 0 with 80% accuracy
            { 80, 0 }     // mic 1 hits part 0 with 80% accuracy
        };
        uint[] phraseTicksTotal = { 100, 100 };

        var (assignment, meters) = YargFreeVocalsEngine.ComputeBestAssignment(
            micPartHits, phraseTicksTotal, AwesomeThreshold);

        // Expect both mics assigned to part 0
        Assert.AreEqual(new int[] { 0, 0 }, assignment);

        // Expect meter capped at 1.0 (not 1.6)
        Assert.AreEqual(1.0, meters[0], Epsilon);
        Assert.AreEqual(0.0, meters[1], Epsilon);
    }

    // AC5.3: Unassigned mic contributes nothing
    [Test]
    public void BestAssignment_UnassignedMic_ContributesNothing()
    {
        // 2 mics, 2 parts; only mic 0 has hits on part 0, mic 1 has no hits
        double[,] micPartHits =
        {
            { 100, 0 },   // mic 0 hits part 0
            { 0, 0 }      // mic 1 has no hits
        };
        uint[] phraseTicksTotal = { 100, 100 };

        var (assignment, meters) = YargFreeVocalsEngine.ComputeBestAssignment(
            micPartHits, phraseTicksTotal, AwesomeThreshold);

        // The optimal assignment should assign mic 0 to part 0 and leave mic 1 unassigned
        // However, the algorithm might assign mic 1 to part 0 since it doesn't hurt (adds 0)
        Assert.AreEqual(0, assignment[0], "mic 0 should be assigned to part 0");

        // Mic 1 should ideally be unassigned, but if assigned to part 0, it adds nothing
        Assert.That(assignment[1], Is.EqualTo(-1).Or.EqualTo(0),
            "mic 1 should be unassigned or assigned to part 0 (but adds nothing)");

        // Expect only part 0 meter filled (mic 1 assignment doesn't contribute)
        Assert.AreEqual(1.0, meters[0], Epsilon);
        Assert.AreEqual(0.0, meters[1], Epsilon);
    }

    // Per-window behavior: single mic non-overlapping fills both meters
    [Test]
    public void PerWindow_SingleMicNonOverlapping_FillsBothMeters()
    {
        uint[] phraseTicksTotal = { 100, 100 };
        double awesomeThreshold = 0.5;

        // Window 1: mic 0 hits part 0 only
        double[,] window1Hits =
        {
            { 50, 0 }   // 50% of phrase ticks
        };

        var (assignment1, meters1) = YargFreeVocalsEngine.ComputeBestAssignment(
            window1Hits, phraseTicksTotal, awesomeThreshold);

        // Expect mic 0 assigned to part 0, meter[0] = 0.5
        Assert.AreEqual(new int[] { 0 }, assignment1);
        Assert.AreEqual(0.5, meters1[0], Epsilon);
        Assert.AreEqual(0.0, meters1[1], Epsilon);

        // Window 2: mic 0 hits part 1 only (different window)
        double[,] window2Hits =
        {
            { 0, 50 }   // 50% of phrase ticks
        };

        var (assignment2, meters2) = YargFreeVocalsEngine.ComputeBestAssignment(
            window2Hits, phraseTicksTotal, awesomeThreshold);

        // Expect mic 0 assigned to part 1, meter[1] = 0.5
        Assert.AreEqual(new int[] { 1 }, assignment2);
        Assert.AreEqual(0.0, meters2[0], Epsilon);
        Assert.AreEqual(0.5, meters2[1], Epsilon);

        // Simulate cumulative state (as the engine would do)
        double[] cumulativeMeters = { 0.0, 0.0 };

        // Add window 1 contribution
        for (int i = 0; i < assignment1.Length; i++)
        {
            int part = assignment1[i];
            if (part >= 0)
                cumulativeMeters[part] += window1Hits[i, part] / (double)phraseTicksTotal[part];
        }

        // Add window 2 contribution
        for (int i = 0; i < assignment2.Length; i++)
        {
            int part = assignment2[i];
            if (part >= 0)
                cumulativeMeters[part] += window2Hits[i, part] / (double)phraseTicksTotal[part];
        }

        // Cap at 1.0
        for (int j = 0; j < cumulativeMeters.Length; j++)
        {
            if (cumulativeMeters[j] > 1.0) cumulativeMeters[j] = 1.0;
        }

        // After both windows, both meters should be at 0.5 (double awesome)
        Assert.AreEqual(0.5, cumulativeMeters[0], Epsilon);
        Assert.AreEqual(0.5, cumulativeMeters[1], Epsilon);
    }

    // Sub-threshold equal hits across two mics on two parts: prefer spreading
    // across distinct parts so both meters move, rather than collapsing both
    // mics onto part 0 (which the prior pure-sum tiebreak chose lexically).
    [Test]
    public void BestAssignment_SubThreshold_PrefersDistinctParts()
    {
        // Both mics hit both parts equally; neither sum can cross threshold this window.
        double[,] micPartHits =
        {
            { 20, 20 },
            { 20, 20 }
        };
        uint[] phraseTicksTotal = { 100, 100 };
        double awesomeThreshold = 0.6;

        var (assignment, meters) = YargFreeVocalsEngine.ComputeBestAssignment(
            micPartHits, phraseTicksTotal, awesomeThreshold);

        // Distinct-parts tiebreak should pick [0, 1] over [0, 0]; both meters get filled.
        Assert.AreEqual(new int[] { 0, 1 }, assignment);
        Assert.AreEqual(0.2, meters[0], Epsilon);
        Assert.AreEqual(0.2, meters[1], Epsilon);
    }
}