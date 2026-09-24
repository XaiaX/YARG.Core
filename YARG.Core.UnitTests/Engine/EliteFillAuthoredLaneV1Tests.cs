using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;

namespace YARG.Core.UnitTests.Engine;

/// <summary>
/// Coverage for authored Elite lane identity in the V1 runtime scheduler. Authored
/// membership — not flattened generic lane flags — is the runtime lane state, so
/// overlapping hand lanes (e.g. authored Green and Blue lanes) each track continuation
/// independently and a foreign lane boundary can neither grant nor revoke coverage.
/// </summary>
public sealed class EliteFillAuthoredLaneV1Tests
{
    private static DrumNote Note(double time, FourLaneDrumPad pad, bool laneStart = false, bool laneEnd = false,
        EliteDrumConversionOrigin? origin = null)
    {
        var flags = NoteFlags.Tremolo;
        if (laneStart) flags |= NoteFlags.LaneStart;
        if (laneEnd) flags |= NoteFlags.LaneEnd;
        return new DrumNote(pad, DrumNoteType.Neutral, DrumNoteFlags.None, flags,
            time, (uint) Math.Round(time * 960), false, DrumStem.Else, origin);
    }

    private static EliteDrumConversionOrigin Origin(int ordinal, int pad, double time)
        => new(new EliteDrumSourceDefinition($"lane-fixture-{ordinal}", ordinal, pad,
            (uint) Math.Round(time * 960), 0, time));

    private static EliteFillAuthoredLane Lane(string id, params DrumNote[] members)
        => new(id, members);

    private static EliteFillAuthoredLaneMap Map(params EliteFillAuthoredLane[] lanes)
        => new(lanes);

    [Test]
    public void LateInputRefresh_DoesNotEraseCoverageForEarlierUnresolvedMember()
    {
        var start = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var earlier = Note(1.1, FourLaneDrumPad.GreenDrum);
        var refresh = Note(1.2, FourLaneDrumPad.GreenDrum);
        var end = Note(1.3, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(new[] { start, earlier, refresh, end },
            EliteFillPolicyV1.Default, null, Map(Lane("green", start, earlier, refresh, end)));
        scheduler.SetLaneAutohitWindow(0.160);

        scheduler.TryAdjudicate(start, true, 1.0, out _);
        scheduler.RecordAuthoredLaneInput(start, 1.0);
        scheduler.TryAdjudicate(refresh, true, 1.2, out _);
        scheduler.RecordAuthoredLaneInput(refresh, 1.2);

        Assert.That(scheduler.IsCadenceCovered(earlier), Is.True,
            "native lane expiry coverage only checks whether note time is before the refreshed expiry");
    }

    [Test]
    public void FindCandidate_ChoosesEarliestEligibleSamePadNote()
    {
        var earlier = Note(1.0, FourLaneDrumPad.GreenDrum);
        var nearerFuture = Note(1.05, FourLaneDrumPad.GreenDrum);
        var scheduler = new EliteFillRuntimeScheduler(new[] { earlier, nearerFuture }, EliteFillPolicyV1.Default);

        var candidate = scheduler.FindCandidate((int) FourLaneDrumPad.GreenDrum, 1.05,
            _ => (-0.07, 0.10));

        Assert.That(candidate, Is.SameAs(earlier),
            "overlapping eligibility must not allow an input to skip an earlier unresolved same-pad note");
    }

    [Test]
    public void PriorPhraseBarrier_AllowsOnlyNewAuthoredEntryLaneToRecoverCadence()
    {
        var firstStart = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var firstMember = Note(1.1, FourLaneDrumPad.GreenDrum);
        var firstEnd = Note(1.2, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var nextStart = Note(1.3, FourLaneDrumPad.BlueDrum, laneStart: true);
        var nextMember = Note(1.4, FourLaneDrumPad.BlueDrum);
        var nextEnd = Note(1.5, FourLaneDrumPad.BlueDrum, laneEnd: true);
        var genericStart = Note(1.32, FourLaneDrumPad.RedDrum, laneStart: true);
        var genericMember = Note(1.42, FourLaneDrumPad.RedDrum);
        var genericEnd = Note(1.52, FourLaneDrumPad.RedDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(
            new[] { firstStart, firstMember, firstEnd, nextStart, genericStart, nextMember,
                genericMember, nextEnd, genericEnd },
            EliteFillPolicyV1.Default, null,
            Map(Lane("first", firstStart, firstMember, firstEnd), Lane("next", nextStart, nextMember, nextEnd)));
        scheduler.SetLaneAutohitWindow(0.160);

        scheduler.TryAdjudicate(firstStart, true, 1.0, out _);
        scheduler.RecordAuthoredLaneInput(firstStart, 1.0);
        scheduler.LatchBarrier(1.05);
        scheduler.TryAdjudicate(nextStart, true, 1.3, out _);
        scheduler.RecordAuthoredLaneInput(nextStart, 1.3);
        scheduler.TryAdjudicate(genericStart, true, 1.32, out _);
        Assert.That(scheduler.BarrierPhase(1.5), Is.EqualTo(EliteFillBarrierPhase.Recovering));
        scheduler.ResolveCadence(1.5);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.IsTerminal(firstMember), Is.False,
                "a lane entered before the barrier remains suppressed during recovery");
            Assert.That(scheduler.IsTerminal(nextMember), Is.True,
                "a successful new authored lane entry restores only that lane's normal cadence");
            Assert.That(scheduler.IsTerminal(genericMember), Is.False,
                "a successfully entered flag-only lane remains suppressed during recovery");
            Assert.That(scheduler.BarrierPhase(1.5), Is.EqualTo(EliteFillBarrierPhase.Recovering),
                "lane continuation does not clear the participation barrier");
        }

        var oldExpiry = scheduler.Expire(scheduler.DeadlineAt(firstMember).GraceUntil + 0.001);
        Assert.That(oldExpiry.Any(commit => ReferenceEquals(commit.Note, firstMember) && !commit.WasHit), Is.True,
            "an older lane still expires as a miss through the recovery period");
    }

    [Test]
    public void OverlappingLanes_NoInputFreeCoverage_EvenAfterBothEntriesHit()
    {
        // Chart order G1(S) B1(S) G2 B2 G3(E) B3(E): entering both lanes with real
        // inputs never adjudicates a later member on its own. Authored membership
        // carries no input-free cadence; later members require qualifying input.
        var g1 = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var b1 = Note(1.1, FourLaneDrumPad.BlueDrum, laneStart: true);
        var g2 = Note(1.2, FourLaneDrumPad.GreenDrum);
        var b2 = Note(1.3, FourLaneDrumPad.BlueDrum);
        var g3 = Note(1.4, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var b3 = Note(1.5, FourLaneDrumPad.BlueDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(new[] { g1, b1, g2, b2, g3, b3 },
            EliteFillPolicyV1.Default, null,
            Map(Lane("green", g1, g2, g3), Lane("blue", b1, b2, b3)));

        scheduler.TryAdjudicate(g1, true, 1.0, out _);
        scheduler.RecordAuthoredLaneInput(g1, 1.0);
        scheduler.TryAdjudicate(b1, true, 1.1, out _);
        scheduler.RecordAuthoredLaneInput(b1, 1.1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.IsCadenceCovered(g2), Is.False,
                "an entered authored lane never grants input-free cadence coverage");
            Assert.That(scheduler.IsCadenceCovered(b2), Is.False);
            Assert.That(scheduler.IsCadenceCovered(g3), Is.False);
            Assert.That(scheduler.IsCadenceCovered(b3), Is.False);
        }

        // Expiry must therefore miss the uncovered later members rather than
        // adjudicating them as hits.
        scheduler.Expire(5.0);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.IsTerminal(g2) && scheduler.IsTerminal(b2)
                && scheduler.IsTerminal(g3) && scheduler.IsTerminal(b3), Is.True);
            Assert.That(scheduler.ResolveCadence(5.0), Is.Empty);
        }
    }

    [Test]
    public void Expiry_PreservesCoveredAuthoredMemberBeforeMarkingItTerminal()
    {
        var start = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var covered = Note(1.1, FourLaneDrumPad.GreenDrum);
        var end = Note(1.2, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(new[] { start, covered, end },
            EliteFillPolicyV1.Default, null, Map(Lane("green", start, covered, end)));
        scheduler.SetLaneAutohitWindow(0.160);
        scheduler.TryAdjudicate(start, true, 1.0, out _);
        scheduler.RecordAuthoredLaneInput(start, 1.0);

        Assert.That(scheduler.IsCadenceCovered(covered), Is.True);
        var commits = scheduler.Expire(scheduler.DeadlineAt(covered).GraceUntil + 0.001);
        Assert.That(commits.Select(commit => (commit.Note, commit.WasHit)),
            Does.Contain((covered, true)), "expiry must evaluate coverage before terminalizing the member");
    }

    [Test]
    public void OverlappingLanes_ProtectionIsPerLaneAndInputDriven()
    {
        var g1 = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var b1 = Note(1.1, FourLaneDrumPad.BlueDrum, laneStart: true);
        var g2 = Note(1.2, FourLaneDrumPad.GreenDrum);
        var b2 = Note(1.3, FourLaneDrumPad.BlueDrum);
        var g3 = Note(1.4, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var b3 = Note(1.5, FourLaneDrumPad.BlueDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(new[] { g1, b1, g2, b2, g3, b3 },
            EliteFillPolicyV1.Default, null,
            Map(Lane("green", g1, g2, g3), Lane("blue", b1, b2, b3)));

        // Only the green lane is entered by a real input.
        scheduler.TryAdjudicate(g1, true, 1.0, out _);
        scheduler.RecordAuthoredLaneInput(g1, 1.0);

        const double window = 0.160; // native HitWindow.LaneAutohitWindow policy
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.GreenDrum, 1.1, window),
                Is.True, "a pad of an entered, refreshed lane is protected");
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.BlueDrum, 1.1, window),
                Is.False, "the unentered blue lane protects nothing");
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.RedDrum, 1.1, window),
                Is.False, "an off-lane pad is never protected");
        }

        // An unentered lane cannot be refreshed by pad input, but a protected strike on
        // the entered lane refreshes only that lane.
        scheduler.RecordAuthoredLanePadInput((int) FourLaneDrumPad.BlueDrum, 1.1);
        Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.BlueDrum, 1.15, window), Is.False);
        scheduler.RecordAuthoredLanePadInput((int) FourLaneDrumPad.GreenDrum, 1.15);
        Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.GreenDrum, 1.3, window), Is.True);

        // Native-style active-pad acceptance is independent of hit forgiveness expiry.
        // A late matching strike is harmless and refreshes cadence without scoring.
        Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.GreenDrum, 1.45, window), Is.True);
        scheduler.RecordAuthoredLanePadInput((int) FourLaneDrumPad.GreenDrum, 1.45);
        Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.RedDrum, 1.46, window), Is.False);

        // Entering the blue lane later turns its pad protectable.
        scheduler.TryAdjudicate(b1, true, 1.5, out _);
        scheduler.RecordAuthoredLaneInput(b1, 1.5);
        Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.BlueDrum, 1.55, window), Is.True);
    }

    [Test]
    public void FinalMemberTerminal_EndsLaneActivityEvenWhenEarlierMemberIsUnresolved()
    {
        var g1 = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var b1 = Note(1.1, FourLaneDrumPad.BlueDrum, laneStart: true);
        var g2 = Note(1.2, FourLaneDrumPad.GreenDrum);
        var b2 = Note(1.3, FourLaneDrumPad.BlueDrum);
        var g3 = Note(1.4, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var b3 = Note(1.5, FourLaneDrumPad.BlueDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(new[] { g1, b1, g2, b2, g3, b3 },
            EliteFillPolicyV1.Default, null, Map(Lane("green", g1, g2, g3), Lane("blue", b1, b2, b3)));

        scheduler.TryAdjudicate(g1, true, 1.0, out _);
        scheduler.RecordAuthoredLaneInput(g1, 1.0);
        scheduler.TryAdjudicate(b1, true, 1.1, out _);
        scheduler.RecordAuthoredLaneInput(b1, 1.1);
        scheduler.TryAdjudicate(g3, true, 1.2, out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.IsTerminal(g2), Is.False, "earlier member remains unresolved");
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.GreenDrum, 1.21, 0.16),
                Is.False, "the green lane ends as soon as its final member resolves");
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.BlueDrum, 1.21, 0.16),
                Is.True, "another active lane continues to protect its own pad");
        }
    }

    [Test]
    public void PostEndLeniency_AllowsEarlySignedTimestampAndUsesStrictBoundary()
    {
        var g1 = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var g2 = Note(2.0, FourLaneDrumPad.GreenDrum);
        var g3 = Note(3.0, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(new[] { g1, g2, g3 }, EliteFillPolicyV1.Default, null,
            Map(Lane("green", g1, g2, g3)));
        scheduler.TryAdjudicate(g1, true, 1.0, out _);
        scheduler.RecordAuthoredLaneInput(g1, 1.0);
        scheduler.TryAdjudicate(g3, true, 3.0, out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.GreenDrum, 2.9, 0, 0.25),
                Is.True, "the native signed delta permits protection before the final note's chart time");
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.GreenDrum, 3.25, 0, 0.25),
                Is.False, "the proximity boundary is strict");
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.GreenDrum, 2.9, 0, 0),
                Is.False, "zero proximity window disables post-end leniency");
        }
    }

    [Test]
    public void MultiLaneMembership_ProtectionAndRefreshFollowAnyMemberLane()
    {
        // m1 starts the blue lane but is also a green lane member, so striking the blue
        // pad is protected through the entered green lane (arbitrary membership), and a
        // hit on a shared member refreshes both lanes that contain it.
        var g1 = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var m1 = Note(1.1, FourLaneDrumPad.BlueDrum, laneStart: true);
        var m2 = Note(1.2, FourLaneDrumPad.BlueDrum);
        var g2 = Note(1.3, FourLaneDrumPad.GreenDrum);
        var m3 = Note(1.4, FourLaneDrumPad.BlueDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(new[] { g1, m1, m2, g2, m3 },
            EliteFillPolicyV1.Default, null,
            Map(Lane("green", g1, m1, m2, g2), Lane("blue", m1, m2, m3)));

        scheduler.TryAdjudicate(g1, true, 1.0, out _);
        scheduler.RecordAuthoredLaneInput(g1, 1.0);

        const double window = 0.160;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.IsCadenceCovered(m1), Is.False,
                "multi-lane membership still grants no input-free coverage");
            Assert.That(scheduler.IsCadenceCovered(m2), Is.False);
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.BlueDrum, 1.1, window),
                Is.True, "the blue pad is protected through the entered green lane that contains m1/m2");
        }

        // A real hit on shared member m1 refreshes every lane containing it.
        scheduler.TryAdjudicate(m1, true, 1.1, out _);
        scheduler.RecordAuthoredLaneInput(m1, 1.1);
        Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.BlueDrum, 1.2, window), Is.True);
    }

    [Test]
    public void LaneStartNote_IsNeverCoveredByItsOwnLane()
    {
        var g1 = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var g2 = Note(1.1, FourLaneDrumPad.GreenDrum);
        var g3 = Note(1.2, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(new[] { g1, g2, g3 },
            EliteFillPolicyV1.Default, null, Map(Lane("green", g1, g2, g3)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.IsCadenceCovered(g1), Is.False,
                "a lane entry always requires a real input, even after later members resolve");
            Assert.That(scheduler.IsCadenceCovered(g2), Is.False,
                "coverage waits for the lane entry, not merely for membership");
        }
    }

    [Test]
    public void LaneWithMissingMembers_ClaimedNotesGetNoFlagFallback()
    {
        var g1 = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var b1 = Note(1.1, FourLaneDrumPad.BlueDrum, laneStart: true);
        var g2 = Note(1.2, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var b2 = Note(1.3, FourLaneDrumPad.BlueDrum, laneEnd: true);
        var foreign = Note(9.0, FourLaneDrumPad.BlueDrum);
        var scheduler = new EliteFillRuntimeScheduler(new[] { g1, b1, g2, b2 },
            EliteFillPolicyV1.Default, null,
            // Both lanes reference a note outside the journal: they are dropped entirely
            // rather than granting partial coverage, and their claimed members must not
            // re-enter through the generic flag fallback.
            Map(Lane("green", g1, g2, foreign), Lane("blue", b1, b2, foreign)));

        scheduler.TryAdjudicate(g1, true, 1.0, out _);
        scheduler.TryAdjudicate(b1, true, 1.1, out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.AuthoredLanes, Is.Empty);
            Assert.That(scheduler.IsCadenceCovered(g2), Is.False,
                "a dropped authored lane's members never fall back to generic flags");
            Assert.That(scheduler.IsCadenceCovered(b2), Is.False,
                "no authored forgiveness is granted for the dropped lane");
        }
    }

    [Test]
    public void ChartWithoutAuthoredLanes_KeepsFlagBehavior()
    {
        var g1 = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var b1 = Note(1.1, FourLaneDrumPad.BlueDrum);
        var g2 = Note(1.2, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var scheduler = new EliteFillRuntimeScheduler(new[] { g1, b1, g2 }, EliteFillPolicyV1.Default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.AuthoredLanes, Is.Empty);
            Assert.That(scheduler.IsCadenceCovered(g2), Is.False);
        }

        scheduler.TryAdjudicate(g1, true, 1.0, out _);
        Assert.That(scheduler.IsCadenceCovered(g2), Is.True);
    }

    [Test]
    public void LaneMap_ResolvesMembersFromAuthoredPhraseRecords()
    {
        var origins = new[]
        {
            Origin(0, (int) FourLaneDrumPad.GreenDrum, 1.0),
            Origin(1, (int) FourLaneDrumPad.BlueDrum, 1.1),
            Origin(2, (int) FourLaneDrumPad.GreenDrum, 1.2),
            Origin(3, (int) FourLaneDrumPad.BlueDrum, 1.3),
            Origin(4, (int) FourLaneDrumPad.GreenDrum, 1.4),
            Origin(5, (int) FourLaneDrumPad.BlueDrum, 1.5),
        };
        var physical = new[]
        {
            Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true, origin: origins[0]),
            Note(1.1, FourLaneDrumPad.BlueDrum, laneStart: true, origin: origins[1]),
            Note(1.2, FourLaneDrumPad.GreenDrum, origin: origins[2]),
            Note(1.3, FourLaneDrumPad.BlueDrum, origin: origins[3]),
            Note(1.4, FourLaneDrumPad.GreenDrum, laneEnd: true, origin: origins[4]),
            Note(1.5, FourLaneDrumPad.BlueDrum, laneEnd: true, origin: origins[5]),
        };

        var records = new[]
        {
            MakeRecord("green", new[] { origins[0], origins[2], origins[4] }),
            MakeRecord("blue", new[] { origins[1], origins[3], origins[5] }),
            // Unresolved records are provenance only and must not carry runtime lanes.
            MakeRecord("malformed", new[] { origins[0], origins[1], origins[2] }, resolved: false),
            // A record with too few surviving members is dropped (fail closed).
            MakeRecord("short", new[] { origins[0], origins[1] }),
            // Origins that no surviving note carries resolve to no members: dropped.
            MakeRecord("dropped", new[] { Origin(99, (int) FourLaneDrumPad.RedDrum, 2.0),
                Origin(98, (int) FourLaneDrumPad.RedDrum, 2.1), Origin(97, (int) FourLaneDrumPad.RedDrum, 2.2) }),
        };

        var map = EliteFillAuthoredLaneMap.Build(physical, records);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(map.Lanes.Select(lane => lane.GameplayId), Is.EqualTo(new[] { "green", "blue" }));
            Assert.That(map.Lanes[0].Members, Is.EqualTo(new[] { physical[0], physical[2], physical[4] }));
            Assert.That(map.Lanes[1].Members, Is.EqualTo(new[] { physical[1], physical[3], physical[5] }));
            // Every record claims its surviving members — resolved, unresolved, short,
            // or matching no lane at all — so no authored note may fall back to flags.
            Assert.That(map.AuthoredMembershipNotes, Is.EquivalentTo(physical));
        }
    }

    [Test]
    public void LaneMap_DuplicateOrigins_AreDeduplicatedBeforeMemberCount()
    {
        var origins = new[]
        {
            Origin(0, (int) FourLaneDrumPad.GreenDrum, 1.0),
            Origin(1, (int) FourLaneDrumPad.GreenDrum, 1.1),
            Origin(2, (int) FourLaneDrumPad.GreenDrum, 1.2),
        };
        var physical = new[]
        {
            Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true, origin: origins[0]),
            Note(1.1, FourLaneDrumPad.GreenDrum, origin: origins[1]),
            Note(1.2, FourLaneDrumPad.GreenDrum, laneEnd: true, origin: origins[2]),
        };

        // The same origin repeated three times counts as one member, not three: the
        // lane is malformed (1 unique member) and must be skipped without throwing.
        EliteFillAuthoredLaneMap map = null!;
        Assert.DoesNotThrow(() =>
            map = EliteFillAuthoredLaneMap.Build(physical, new[]
            {
                MakeRecord("duplicated", new[] { origins[0], origins[0], origins[0] }),
            }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(map.Lanes, Is.Empty,
                "a lane below the unique-member minimum is dropped, not constructed");
            Assert.That(map.AuthoredMembershipNotes, Is.EquivalentTo(new[] { physical[0] }),
                "the duplicated origin still claims its surviving note");
        }

        // Duplicates that still leave the minimum of unique members keep the lane.
        var deduped = EliteFillAuthoredLaneMap.Build(physical, new[]
        {
            MakeRecord("deduplicated", new[] { origins[0], origins[1], origins[2], origins[1] }),
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deduped.Lanes.Select(lane => lane.GameplayId), Is.EqualTo(new[] { "deduplicated" }));
            Assert.That(deduped.Lanes[0].Members, Is.EqualTo(physical),
                "each physical member is counted exactly once regardless of origin repeats");
        }
    }

    [Test]
    public void UnresolvedAuthoredRecord_GrantsNoLaneAndNoFlagFallback()
    {
        // A 3-origin authored hand lane whose record is unresolved (for example by
        // descriptor conditions) still has generic lane flags stamped by the loader.
        // Its notes are claimed membership: no lane, and no flag fallback either.
        var origins = new[]
        {
            Origin(0, (int) FourLaneDrumPad.GreenDrum, 1.0),
            Origin(1, (int) FourLaneDrumPad.GreenDrum, 1.1),
            Origin(2, (int) FourLaneDrumPad.GreenDrum, 1.2),
        };
        var physical = new[]
        {
            Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true, origin: origins[0]),
            Note(1.1, FourLaneDrumPad.GreenDrum, origin: origins[1]),
            Note(1.2, FourLaneDrumPad.GreenDrum, laneEnd: true, origin: origins[2]),
        };
        var map = EliteFillAuthoredLaneMap.Build(physical,
            new[] { MakeRecord("unresolved", origins, resolved: false) });

        var scheduler = new EliteFillRuntimeScheduler(physical, EliteFillPolicyV1.Default, null, map);
        scheduler.TryAdjudicate(physical[0], true, 1.0, out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(map.Lanes, Is.Empty, "an unresolved record carries no runtime lane");
            Assert.That(map.AuthoredMembershipNotes, Is.EquivalentTo(physical));
            Assert.That(scheduler.AuthoredLanes, Is.Empty);
            Assert.That(scheduler.IsCadenceCovered(physical[1]), Is.False,
                "the later member gets no automatic cadence coverage through generic flags");
            Assert.That(scheduler.IsCadenceCovered(physical[2]), Is.False,
                "no implicit continuation is granted for unresolved authored membership");
        }
    }

    [Test]
    public void OverlappingValidAndUnresolvedMembership_NeitherGrantsInputFreeCoverage()
    {
        // Green resolves to a valid lane; blue's record stays unresolved while the
        // loader stamped generic flags for it. Neither membership grants input-free
        // cadence, and the unresolved blue members fail closed with no flag fallback.
        // Only the valid green lane can protect a real in-lane strike.
        var g1 = Note(1.0, FourLaneDrumPad.GreenDrum, laneStart: true);
        var b1 = Note(1.1, FourLaneDrumPad.BlueDrum, laneStart: true);
        var g2 = Note(1.2, FourLaneDrumPad.GreenDrum);
        var b2 = Note(1.3, FourLaneDrumPad.BlueDrum);
        var g3 = Note(1.4, FourLaneDrumPad.GreenDrum, laneEnd: true);
        var b3 = Note(1.5, FourLaneDrumPad.BlueDrum, laneEnd: true);
        var map = new EliteFillAuthoredLaneMap(
            new[] { Lane("green", g1, g2, g3) },
            new[] { g1, g2, g3, b1, b2, b3 });
        var scheduler = new EliteFillRuntimeScheduler(new[] { g1, b1, g2, b2, g3, b3 },
            EliteFillPolicyV1.Default, null, map);

        scheduler.TryAdjudicate(g1, true, 1.0, out _);
        scheduler.RecordAuthoredLaneInput(g1, 1.0);

        const double window = 0.160;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.IsCadenceCovered(g2), Is.False,
                "valid authored membership is not input-free coverage either");
            Assert.That(scheduler.IsCadenceCovered(g3), Is.False);
            Assert.That(scheduler.IsCadenceCovered(b2), Is.False,
                "invalid membership never borrows the overlapping valid lane");
            Assert.That(scheduler.IsCadenceCovered(b3), Is.False,
                "invalid membership gets no generic flag fallback either");
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.GreenDrum, 1.1, window),
                Is.True, "the valid lane protects real in-lane strikes while refreshed");
            Assert.That(scheduler.IsAuthoredLaneStrikeProtected((int) FourLaneDrumPad.BlueDrum, 1.1, window),
                Is.False, "the unresolved lane protects nothing");
        }
    }

    private static EliteDrumVisualDescriptorV1 MakeRecord(string id, EliteDrumConversionOrigin[] origins,
        bool resolved = true)
    {
        var finalPad = resolved
            ? new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int) FourLaneDrumPad.GreenDrum)
            : EliteDrumFinalPadIdentity.UnresolvedFor(Instrument.ProDrums);
        return new EliteDrumVisualDescriptorV1(id, origins.Select(origin => origin.Source.SourceId), origins,
            finalPad, new EliteDrumComponentVisualDescriptor(id, true, id, 0, "ProDrums",
                Array.Empty<EliteDrumComponentMetadata>()),
            0, 0, 0, 0, 0, 0, true, false);
    }
}
