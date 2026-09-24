using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

/// <summary>
/// Dedicated actual-engine coverage for Elite Fill V1. The fixture uses the public
/// provenance-bearing DrumNote constructor, so these tests exercise the same metadata
/// gate used by converted Elite charts without involving loaders or a fake scheduler.
/// </summary>
public sealed class EliteFillEngineIntegrationV1Tests
{
    private const int Resolution = 480;
    private const double Beat = 0.5;

    private static readonly float[] StarThresholds = { 0.06f, 0.12f, 0.2f, 0.45f, 0.75f, 1.09f };
    private static readonly float[] SoloThresholds = { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f };

    [TestCase(0.000, EliteFillWindow.Entry)]
    [TestCase(0.100, EliteFillWindow.Entry)]
    [TestCase(0.101, EliteFillWindow.Grace)]
    [TestCase(0.150, EliteFillWindow.Grace)]
    [TestCase(0.151, EliteFillWindow.Outside)]
    public void EliteFillEntryDeadlineTable(double offset, EliteFillWindow expected)
    {
        var (engine, notes) = Build(new[] { NoteSpec.At(1.0, FourLaneDrumPad.RedDrum) });
        engine.Update(1.0);
        var deadline = EliteFillDeadlineCalculator.Calculate(notes.Notes[0].Time,
            new EliteFillPolicyParameters(entryWindowSeconds: 0.100));

        Assert.That(deadline.Classify(notes.Notes[0].Time + offset), Is.EqualTo(expected));
    }

    [Test]
    public void HeldEntry_LaterNoteCommitsOnlyAfterEarlierPhysicalNoteResolves()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.At(1.000, FourLaneDrumPad.RedDrum),
            NoteSpec.At(1.020, FourLaneDrumPad.YellowDrum),
        });

        var hitCallbacks = new List<DrumNote>();
        engine.OnNoteHit += (_, note) => hitCallbacks.Add(note);
        engine.Update(0.9);
        Hit(engine, 1.020, DrumsAction.YellowDrum);

        Assert.That(notes.Notes[1].WasHit, Is.False,
            "adjudication is terminal, but the later physical note remains uncommitted");
        Assert.That(notes.Notes[0].WasHit, Is.False);
        Assert.That(engine.EngineStats.NotesHit, Is.Zero,
            "the later terminal must remain pending in the ordered score journal");

        engine.Update(1.0 + 0.151);
        Assert.That(notes.Notes[0].WasMissed, Is.True);
        Assert.That(notes.Notes[1].WasHit, Is.True);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Assert.That(hitCallbacks, Is.EqualTo(new[] { notes.Notes[1] }));
    }

    [Test]
    public void DuplicateInput_DoesNotDuplicateTerminalOrScore()
    {
        var (engine, notes) = Build(new[] { NoteSpec.At(1.0, FourLaneDrumPad.RedDrum) });
        Hit(engine, 1.0, DrumsAction.RedDrum);
        Hit(engine, 1.001, DrumsAction.RedDrum);
        engine.Update(1.2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes.Notes[0].WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.Overhits, Is.Zero,
                "a duplicate terminal input must not create an additional overhit");
        }
    }

    [Test]
    public void Cadence_GYBAndGYYB_AreAcceptedByActualEngineAtTheirEntryTimes()
    {
        var gyb = Build(new[]
        {
            NoteSpec.At(1.0, FourLaneDrumPad.RedDrum),
            NoteSpec.At(1.1, FourLaneDrumPad.YellowDrum),
            NoteSpec.At(1.2, FourLaneDrumPad.BlueDrum),
        });
        Hit(gyb.Engine, 1.0, DrumsAction.RedDrum);
        Hit(gyb.Engine, 1.1, DrumsAction.YellowDrum);
        Hit(gyb.Engine, 1.2, DrumsAction.BlueDrum);

        var gyyb = Build(new[]
        {
            NoteSpec.At(1.0, FourLaneDrumPad.RedDrum),
            NoteSpec.At(1.1, FourLaneDrumPad.YellowDrum),
            NoteSpec.At(1.2, FourLaneDrumPad.YellowDrum),
            NoteSpec.At(1.3, FourLaneDrumPad.BlueDrum),
        });
        Hit(gyyb.Engine, 1.0, DrumsAction.RedDrum);
        Hit(gyyb.Engine, 1.1, DrumsAction.YellowDrum);
        Hit(gyyb.Engine, 1.2, DrumsAction.YellowDrum);
        Hit(gyyb.Engine, 1.3, DrumsAction.BlueDrum);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gyb.Engine.EngineStats.NotesHit, Is.EqualTo(3));
            Assert.That(gyyb.Engine.EngineStats.NotesHit, Is.EqualTo(4));
        }
    }

    [Test]
    public void LiveCadence_GYBAndGYYB_AutoResolveEnteredLaneGems()
    {
        var gyb = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.YellowDrum),
            NoteSpec.Lane(1.2, FourLaneDrumPad.BlueDrum, end: true),
        });
        Hit(gyb.Engine, 1.0, DrumsAction.GreenDrum);
        gyb.Engine.Update(1.301);

        var gyyb = Build(new[]
        {
            NoteSpec.Lane(2.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(2.1, FourLaneDrumPad.YellowDrum),
            NoteSpec.Lane(2.2, FourLaneDrumPad.YellowDrum),
            NoteSpec.Lane(2.3, FourLaneDrumPad.BlueDrum, end: true),
        });
        Hit(gyyb.Engine, 2.0, DrumsAction.GreenDrum);
        gyyb.Engine.Update(2.401);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(gyb.Engine.EngineStats.NotesHit, Is.EqualTo(3));
            Assert.That(gyyb.Engine.EngineStats.NotesHit, Is.EqualTo(4));
        }
    }

    [Test]
    public void LatchedBarrier_OffNoteThenValidInput_RecoversWithoutStrandingChart()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.At(1.0, FourLaneDrumPad.RedDrum),
            NoteSpec.At(1.5, FourLaneDrumPad.YellowDrum),
            NoteSpec.At(2.0, FourLaneDrumPad.BlueDrum),
        });
        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.RedDrum);
        Hit(engine, 1.5, DrumsAction.GreenDrum);
        Hit(engine, 2.0, DrumsAction.BlueDrum);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.EqualTo(1), "an off-note after a committed entry must latch the barrier");
            Assert.That(notes.Notes[2].WasHit, Is.True, "a later valid entry must recover after the barrier");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        }
    }

    [Test]
    public void BarrierChangesActualEngineCadenceTerminalOutcomeButNotRealInputPolicy()
    {
        var clear = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.YellowDrum),
            NoteSpec.Lane(1.2, FourLaneDrumPad.BlueDrum, end: true),
        });
        Hit(clear.Engine, 1.0, DrumsAction.GreenDrum);
        clear.Engine.Update(1.201);

        var blocked = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.YellowDrum),
            NoteSpec.Lane(1.2, FourLaneDrumPad.BlueDrum, end: true),
        });
        Hit(blocked.Engine, 1.0, DrumsAction.GreenDrum);
        Hit(blocked.Engine, 1.05, DrumsAction.RedDrum);
        Hit(blocked.Engine, 1.2, DrumsAction.BlueDrum);
        blocked.Engine.Update(1.301);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clear.Notes.Notes[1].WasHit, Is.True,
                "without a participation barrier, cadence continuation auto-hits the entered lane");
            Assert.That(blocked.Notes.Notes[1].WasHit, Is.False);
            Assert.That(blocked.Notes.Notes[1].WasMissed, Is.True,
                "a latched barrier suppresses automatic continuation and changes the terminal outcome");
            Assert.That(clear.Engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(blocked.Engine.EngineStats.NotesHit, Is.EqualTo(2),
                "the later real input is still scored while the barrier changes only cadence continuation");
        }

        // The later ordinary real hit remains admissible even while the barrier is recovering.
        Assert.That(blocked.Notes.Notes[2].WasHit, Is.True);
    }

    [Test]
    public void OffNote_NoScoreAndLaterValidInputRecovers()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.At(1.0, FourLaneDrumPad.RedDrum),
            NoteSpec.At(2.0, FourLaneDrumPad.BlueDrum),
        });
        int scoreBefore = engine.EngineStats.TotalScore;
        Hit(engine, 1.0, DrumsAction.GreenDrum);

        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(scoreBefore));
        Hit(engine, 2.0, DrumsAction.BlueDrum);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes.Notes[1].WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
            Assert.That(engine.EngineStats.TotalScore, Is.GreaterThan(scoreBefore));
        }
    }

    [Test]
    public void OrderedAdjudication_CommitScoreJournalPreservesChartOrder()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.At(1.000, FourLaneDrumPad.RedDrum),
            NoteSpec.At(1.010, FourLaneDrumPad.YellowDrum),
        });
        var committed = new List<DrumNote>();
        engine.OnNoteHit += (_, note) => committed.Add(note);

        var later = GameInput.Create(1.010, DrumsAction.YellowDrum, 1f);
        var earlier = GameInput.Create(1.010, DrumsAction.RedDrum, 1f);
        engine.QueueInput(ref later);
        engine.QueueInput(ref earlier);
        engine.Update(1.010);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(committed, Is.Empty,
                "both inputs are adjudicated but remain pending until the journal commit deadline");
            Assert.That(engine.EngineStats.NotesHit, Is.Zero);
            engine.Update(1.2);
            Assert.That(committed, Is.EqualTo(new[] { notes.Notes[0], notes.Notes[1] }));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.CommittedScore, Is.GreaterThan(0));
        }
    }

    [Test]
    public void ChordsAndDynamics_ScoreBothPhysicalNotesAndVelocityBonus()
    {
        var chord = NoteSpec.At(1.0, FourLaneDrumPad.RedDrum, DrumNoteType.Accent);
        chord.ChildPads.Add((int) FourLaneDrumPad.YellowDrum);
        var (engine, notes) = Build(new[] { chord });

        Hit(engine, 1.0, DrumsAction.RedDrum, 1.0f);
        Hit(engine, 1.0, DrumsAction.YellowDrum, 1.0f);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes.Notes[0].WasHit, Is.True);
            Assert.That(notes.Notes[0].ChildNotes[0].WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(engine.EngineStats.DynamicsBonus, Is.GreaterThanOrEqualTo(0));
        }
    }

    [Test]
    public void CodaPreBoundaryFlush_ExcludesExactBoundaryInput()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.At(1.0, FourLaneDrumPad.RedDrum),
            NoteSpec.At(2.0, FourLaneDrumPad.YellowDrum),
        }, codaStart: 2.0, codaEnd: 3.0);

        Hit(engine, 1.0, DrumsAction.RedDrum);
        engine.Update(2.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes.Notes[0].WasHit, Is.True);
            Assert.That(notes.Notes[1].WasHit, Is.False);
            Assert.That(notes.Notes[1].WasMissed, Is.False,
                "flush is strictly before the boundary; the exact-boundary note remains available");
        }

        Hit(engine, 2.0, DrumsAction.YellowDrum);
        Assert.That(notes.Notes[1].WasHit, Is.True);
    }

    [Test]
    public void SameTimePermutation_AndCompetingSamePad_ResolveEachPhysicalNoteOnce()
    {
        var first = Build(new[]
        {
            NoteSpec.At(1.0, FourLaneDrumPad.RedDrum),
            NoteSpec.At(1.0, FourLaneDrumPad.YellowDrum),
        });
        Hit(first.Engine, 1.0, DrumsAction.YellowDrum);
        Hit(first.Engine, 1.0, DrumsAction.RedDrum);

        var second = Build(new[]
        {
            NoteSpec.At(1.0, FourLaneDrumPad.RedDrum),
            NoteSpec.At(1.0, FourLaneDrumPad.RedDrum),
        });
        Hit(second.Engine, 1.0, DrumsAction.RedDrum);
        Hit(second.Engine, 1.0, DrumsAction.RedDrum);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(second.Engine.EngineStats.NotesHit, Is.EqualTo(2));
            Assert.That(second.Engine.EngineStats.Overhits, Is.Zero);
        }
    }

    [Test]
    public void HumanKickBeforeHand_ResolvesIndependentPhysicalKickWithoutHandEntry()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.At(1.0, FourLaneDrumPad.Kick),
            NoteSpec.At(1.1, FourLaneDrumPad.RedDrum),
        });

        Hit(engine, 1.0, DrumsAction.Kick);
        Assert.That(notes.Notes[0].WasHit, Is.True,
            "the first physical kick commits immediately without entering a hand lane");
        Hit(engine, 1.1, DrumsAction.RedDrum);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes.Notes[0].WasHit, Is.True);
            Assert.That(notes.Notes[1].WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        }
    }

    [Test]
    public void HumanKickAndHandChord_ResolvesBothPhysicalNotesWithoutFakingCadence()
    {
        var chord = NoteSpec.At(1.0, FourLaneDrumPad.RedDrum);
        chord.ChildPads.Add((int) FourLaneDrumPad.Kick);
        var (engine, notes) = Build(new[] { chord });

        // Deliberately submit kick first. The hand must still be independently accepted.
        Hit(engine, 1.0, DrumsAction.Kick);
        Hit(engine, 1.0, DrumsAction.RedDrum);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes.Notes[0].WasHit, Is.True);
            Assert.That(notes.Notes[0].ChildNotes[0].WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        }
    }

    [Test]
    public void LivePolicy_ExtendsOrdinaryBackendAndAcceptsExactGraceBoundary()
    {
        var extended = Build(new[] { NoteSpec.At(1.0, FourLaneDrumPad.RedDrum) });
        Hit(extended.Engine, 1.09, DrumsAction.RedDrum);
        Assert.That(extended.Notes.Notes[0].WasHit, Is.True,
            "V1 grace extends beyond the ordinary backend for this engine window");

        var boundary = Build(new[] { NoteSpec.At(1.0, FourLaneDrumPad.RedDrum) });
        Hit(boundary.Engine, 1.1, DrumsAction.RedDrum);
        Assert.That(boundary.Notes.Notes[0].WasHit, Is.True,
            "the exact frozen V1 grace boundary remains eligible");
    }

    [Test]
    public void EnteredHandLane_CadenceCoveredGemNeedsNoInput()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.YellowDrum),
            NoteSpec.Lane(1.2, FourLaneDrumPad.BlueDrum, end: true),
        });

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        engine.Update(1.201);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes.Notes[1].WasHit, Is.True,
                "the entered hand lane cadence covers the unpressed middle gem");
            Assert.That(notes.Notes[2].WasHit, Is.False);
            Assert.That(notes.Notes[2].WasMissed, Is.False);
        }
    }

    [Test]
    public void BotAndReset_HitAllThenRestoreFreshRuntime()
    {
        var fixture = Build(new[]
        {
            NoteSpec.At(1.0, FourLaneDrumPad.Kick),
            NoteSpec.At(1.1, FourLaneDrumPad.RedDrum),
        }, isBot: true);
        fixture.Engine.Update(2.0);
        Assert.That(fixture.Engine.EngineStats.NotesHit, Is.EqualTo(2));

        fixture.Engine.Reset();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Engine.EngineStats.NotesHit, Is.Zero);
            Assert.That(fixture.Notes.Notes.All(note => !note.WasHit && !note.WasMissed), Is.True);
        }
    }

    [Test]
    public void UnmatchedInputs_KeepKickNativeProtectionButHandFaultLatchesAuthoredBarrier()
    {
        var kickLaneStart = NoteSpec.At(1.0, FourLaneDrumPad.Kick);
        kickLaneStart.DrumFlags = DrumNoteFlags.KickLane | DrumNoteFlags.KickLaneStart;
        var inKickLane = Build(new[]
        {
            kickLaneStart,
            NoteSpec.Lane(1.5, FourLaneDrumPad.RedDrum, start: true, end: true),
        }, authoredLanes: new[] { new[] { 1 } });
        int inLaneOverhits = 0;
        inKickLane.Engine.OnOverhit += () => inLaneOverhits++;

        Hit(inKickLane.Engine, 1.0, DrumsAction.Kick);
        Hit(inKickLane.Engine, 1.05, DrumsAction.Kick);

        Assert.That(inLaneOverhits, Is.Zero,
            "an extra unmatched kick during a native kick lane must retain native lane protection");

        var offKickLane = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.RedDrum, start: true, end: true),
            NoteSpec.Lane(1.5, FourLaneDrumPad.BlueDrum, start: true, end: true),
        }, authoredLanes: new[] { new[] { 0 }, new[] { 1 } });
        int offLaneOverhits = 0;
        offKickLane.Engine.OnOverhit += () => offLaneOverhits++;

        Hit(offKickLane.Engine, 1.0, DrumsAction.RedDrum);
        Hit(offKickLane.Engine, 1.2, DrumsAction.Kick);

        Assert.That(offLaneOverhits, Is.EqualTo(1),
            "a kick outside any native kick lane still overhits");

        var offHandLane = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.RedDrum, start: true, end: true),
            NoteSpec.Lane(1.5, FourLaneDrumPad.BlueDrum, start: true, end: true),
        }, authoredLanes: new[] { new[] { 0 }, new[] { 1 } });
        Hit(offHandLane.Engine, 1.0, DrumsAction.RedDrum);
        Hit(offHandLane.Engine, 1.2, DrumsAction.YellowDrum);

        Assert.That(GetRuntime(offHandLane.Engine).BarrierPhase(1.2), Is.EqualTo(EliteFillBarrierPhase.Latched),
            "an off-lane hand fault still latches the authored participation barrier");
    }

    [Test]
    public void NativeKickAndLane_AreCharacterizedWithoutConversionMetadata()
    {
        var kick = new DrumNote(FourLaneDrumPad.Kick, DrumNoteType.Neutral, DrumNoteFlags.None,
            NoteFlags.None, 1.0, 960);
        var lane = new DrumNote(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral,
            DrumNoteFlags.None, NoteFlags.Tremolo | NoteFlags.LaneStart | NoteFlags.LaneEnd, 1.5, 1440);
        var diff = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert,
            new List<DrumNote> { kick, lane }, new List<Phrase>(), new List<TextEvent>());
        var engine = CreateEngine(diff, isBot: false);

        using (Assert.EnterMultipleScope())
        {
            Hit(engine, 1.0, DrumsAction.Kick);
            Hit(engine, 1.5, DrumsAction.RedDrum);
            Assert.That(kick.WasHit, Is.True);
            Assert.That(lane.WasHit, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        }
    }

    [Test]
    public void OverlappingLanes_GB_AllInputsHitInChartOrderWithoutOverhitOrBarrier()
    {
        // Authored green and blue lanes overlap: chart order G B G B G B, each lane
        // entered by its own first gem and continued by per-lane cadence.
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(1.2, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(1.3, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(1.4, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(1.5, FourLaneDrumPad.BlueDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 2, 4 }, new[] { 1, 3, 5 } });

        var committed = new List<DrumNote>();
        engine.OnNoteHit += (_, note) => committed.Add(note);
        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        Hit(engine, 1.1, DrumsAction.BlueDrum);
        Hit(engine, 1.2, DrumsAction.GreenDrum);
        Hit(engine, 1.3, DrumsAction.BlueDrum);
        Hit(engine, 1.4, DrumsAction.GreenDrum);
        Hit(engine, 1.5, DrumsAction.BlueDrum);
        engine.Update(1.8);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.Overhits, Is.Zero,
                "valid overlapping-lane cadence must not fall through to native overhit");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(6));
            Assert.That(committed, Is.EqualTo(new[] { notes.Notes[0], notes.Notes[1], notes.Notes[2],
                notes.Notes[3], notes.Notes[4], notes.Notes[5] }),
                "physical notes commit in chart order");
            Assert.That(notes.Notes.All(note => note.WasHit), Is.True);
        }
    }

    [Test]
    public void OverlappingLanes_GB_EntryOnlyInputs_ThenSilence_MissesAllLaterMembers()
    {
        // Both authored lane entries are hit with real input, then complete silence
        // across every remaining frozen deadline. Authored lane cadence must not
        // adjudicate any later member: they expire as misses with no unprompted score.
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(1.2, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(1.3, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(1.4, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(1.5, FourLaneDrumPad.BlueDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 2, 4 }, new[] { 1, 3, 5 } });

        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        Hit(engine, 1.1, DrumsAction.BlueDrum);
        engine.Update(3.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2),
                "only the two really input entries score");
            Assert.That(new[] { notes.Notes[0], notes.Notes[1] }.All(note => note.WasHit), Is.True);
            Assert.That(notes.Notes.Skip(2).All(note => !note.WasHit), Is.True,
                "no later member is auto-hit by lane cadence coverage");
            Assert.That(notes.Notes.Skip(2).All(note => note.WasMissed), Is.True,
                "every later member misses at its frozen deadline");
        }
    }

    [Test]
    public void PracticeSlice_PreservedAuthoredRecordsBoundCadenceToRecentInput()
    {
        var specs = new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.08, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(1.16, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(1.24, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(1.32, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(1.40, FourLaneDrumPad.BlueDrum, end: true),
        };
        var (withoutRecords, _) = Build(specs);
        Hit(withoutRecords, 1.0, DrumsAction.GreenDrum);
        Hit(withoutRecords, 1.08, DrumsAction.BlueDrum);
        withoutRecords.Update(2.0);

        var (withRecords, notes) = Build(specs,
            authoredLanes: new[] { new[] { 0, 2, 4 }, new[] { 1, 3, 5 } });
        Hit(withRecords, 1.0, DrumsAction.GreenDrum);
        Hit(withRecords, 1.08, DrumsAction.BlueDrum);
        withRecords.Update(2.0);

        Assert.That(withoutRecords.EngineStats.NotesHit, Is.GreaterThan(2),
            "practice dropping the records reproduces input-free generic-flag cadence hits");
        Assert.That(withRecords.EngineStats.NotesHit, Is.EqualTo(4),
            "only members inside the native-style input-refreshed lane window are forgiven");
        Assert.That(notes.Notes[2].WasHit && notes.Notes[3].WasHit, Is.True);
        Assert.That(notes.Notes.Skip(4).All(note => note.WasMissed), Is.True,
            "entry alone cannot carry the rest of either authored lane");
    }

    [Test]
    public void OverlappingLanes_GBBGBB_AuthoredBlueMisses_UnclaimedGreenKeepsLegacyFallback()
    {
        // G B B G B B: the blue lane's next member can be forgiven inside the
        // input-refreshed window; later blue members miss. Unclaimed green keeps
        // generic-flag continuation.
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(1.2, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(1.3, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(1.4, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(1.5, FourLaneDrumPad.BlueDrum, end: true),
        }, authoredLanes: new[] { new[] { 1, 2, 4, 5 } });

        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        Hit(engine, 1.1, DrumsAction.BlueDrum);
        engine.Update(3.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero);
            Assert.That(notes.Notes[0].WasHit && notes.Notes[1].WasHit, Is.True);
            Assert.That(notes.Notes[2].WasHit, Is.True,
                "the first blue continuation is within the native lane window");
            Assert.That(new[] { notes.Notes[4], notes.Notes[5] }.All(note => note.WasMissed), Is.True,
                "the authored blue lane expires without more qualifying input");
            Assert.That(notes.Notes[3].WasHit, Is.True,
                "unclaimed green notes keep the legacy generic-flag continuation");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4));
        }
    }

    [Test]
    public void OverlappingLanes_OffPadInput_YieldsExactlyOneNativeOverhitAndBarrier()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(1.2, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(1.3, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(1.4, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(1.5, FourLaneDrumPad.BlueDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 2, 4 }, new[] { 1, 3, 5 } });

        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        Hit(engine, 1.1, DrumsAction.BlueDrum);
        // Red is a genuine off-pad input: no red physical note exists anywhere.
        Hit(engine, 1.15, DrumsAction.RedDrum);
        // Later valid inputs are still adjudicated while the barrier recovers.
        Hit(engine, 1.2, DrumsAction.GreenDrum);
        Hit(engine, 1.3, DrumsAction.BlueDrum);
        Hit(engine, 1.4, DrumsAction.GreenDrum);
        Hit(engine, 1.5, DrumsAction.BlueDrum);
        engine.Update(1.8);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.EqualTo(1),
                "an off-pad input still yields exactly one native overhit");
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(6),
                "every physical note is still resolved by real input after the barrier");
            Assert.That(notes.Notes.All(note => note.WasHit), Is.True);
        }
    }

    [Test]
    public void UnresolvedAuthoredLane_GenericFlagsGrantNoCadenceCoverage()
    {
        // The loader stamps generic LaneStart/LaneEnd/Tremolo flags whenever an authored
        // phrase keeps three or more physical survivors, even when Stage 2 leaves the
        // phrase record explicitly unresolved. Such membership must fail closed: the
        // flags are not a lane, so no auto cadence hit and no implicit continuation.
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(1.2, FourLaneDrumPad.GreenDrum, end: true),
        }, unresolvedLanes: new[] { new[] { 0, 1, 2 } });

        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        engine.Update(1.8);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes.Notes[0].WasHit, Is.True);
            Assert.That(notes.Notes[1].WasHit, Is.False,
                "no auto cadence hit for unresolved authored membership");
            Assert.That(notes.Notes[1].WasMissed, Is.True,
                "the uncovered member expires as a miss, not as silent forgiveness");
            Assert.That(notes.Notes[2].WasHit, Is.False,
                "no implicit continuation through generic lane flags");
            Assert.That(notes.Notes[2].WasMissed, Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1),
                "only the really input entry note is hit");
            Assert.That(overhits, Is.Zero, "the single valid entry input is not an overhit");
        }
    }

    [Test]
    public void OverlappingValidAndUnresolvedLanes_NoInputFreeCoverageForEitherMembership()
    {
        // Overlapping valid + invalid authored membership: neither grants input-free
        // resolution. The green record resolves but its members still require real
        // input; the blue record stays unresolved, so blue members — despite their
        // generic flags — get no cadence forgiveness and no flag fallback.
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(1.2, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(1.3, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(1.4, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(1.5, FourLaneDrumPad.BlueDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 2, 4 } },
            unresolvedLanes: new[] { new[] { 1, 3, 5 } });

        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        engine.Update(3.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1),
                "only the really input entry note scores");
            Assert.That(notes.Notes.All(note => note.WasHit || note.WasMissed), Is.True);
            Assert.That(notes.Notes.Skip(1).All(note => note.WasMissed), Is.True,
                "valid and unresolved authored membership alike miss without input");
        }
    }

    [Test]
    public void AuthoredLanes_ProtectedInLaneStrikesWithNoCandidateAreCompleteNoOps()
    {
        // Wide spacing: candidate windows are [T-0.07, T+0.10], so every strike below
        // (1.15..1.90) is verified to have no eligible same-pad candidate while both
        // lanes stay temporally active and each lane's cadence stays refreshed within
        // the native LaneAutohitWindow (0.160s). Each such strike must be a no-op.
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(2.0, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(2.1, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(3.0, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(3.1, FourLaneDrumPad.BlueDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 2, 4 }, new[] { 1, 3, 5 } });

        var committed = new List<DrumNote>();
        engine.OnNoteHit += (_, note) => committed.Add(note);
        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        Hit(engine, 1.1, DrumsAction.BlueDrum);
        // Protected unmatched in-lane strikes: chained so each pad stays within 0.16s
        // of that lane's last qualifying input.
        Hit(engine, 1.15, DrumsAction.GreenDrum);
        Hit(engine, 1.25, DrumsAction.BlueDrum);
        Hit(engine, 1.30, DrumsAction.GreenDrum);
        Hit(engine, 1.40, DrumsAction.BlueDrum);
        Hit(engine, 1.45, DrumsAction.GreenDrum);
        Hit(engine, 1.55, DrumsAction.BlueDrum);
        Hit(engine, 1.60, DrumsAction.GreenDrum);
        Hit(engine, 1.65, DrumsAction.BlueDrum);
        Hit(engine, 1.75, DrumsAction.GreenDrum);
        Hit(engine, 1.85, DrumsAction.BlueDrum);
        // Later real inputs hit normally.
        Hit(engine, 2.0, DrumsAction.GreenDrum);
        Hit(engine, 2.1, DrumsAction.BlueDrum);
        Hit(engine, 3.0, DrumsAction.GreenDrum);
        Hit(engine, 3.1, DrumsAction.BlueDrum);
        engine.Update(3.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero,
                "protected in-lane strikes cause no barrier and no overhit");
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(6),
                "the extra strikes neither score nor advance any terminal");
            Assert.That(committed, Is.EqualTo(notes.Notes.ToArray()),
                "physical notes commit in chart order");
            Assert.That(notes.Notes.All(note => note.WasHit), Is.True);
        }
    }

    [Test]
    public void AuthoredLane_UnmatchedInputBeforeFirstNoteDoesNotLatchBarrier()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(1.2, FourLaneDrumPad.GreenDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 1, 2 } });
        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 0.5, DrumsAction.BlueDrum);
        Assert.That(GetRuntime(engine).BarrierLatched(0.5), Is.False,
            "an input before the first note must not install a V1 barrier");
        Hit(engine, 1.0, DrumsAction.GreenDrum);
        Hit(engine, 1.1, DrumsAction.GreenDrum);
        Hit(engine, 1.2, DrumsAction.GreenDrum);
        engine.Update(1.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3),
                "a suppressed input must leave normal lane entry and cadence intact");
            Assert.That(notes.Notes.All(note => note.WasHit), Is.True);
            Assert.That(GetRuntime(engine).BarrierLatched(1.5), Is.False);
        }
    }

    [Test]
    public void AuthoredLane_UnmatchedInputDuringWaitCountdownDoesNotLatchBarrier()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(6.0, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(6.1, FourLaneDrumPad.GreenDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 1, 2 } });
        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        var countdownInput = GameInput.Create(2.0, DrumsAction.BlueDrum, 1f);
        engine.QueueInput(ref countdownInput);
        var waitCountdownProperty = typeof(DrumsEngine).GetProperty("IsWaitCountdownActive",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new AssertionException("Could not find IsWaitCountdownActive property.");
        waitCountdownProperty.SetValue(engine, true);
        engine.Update(2.0);
        Assert.That(engine.IsWaitCountdownActive, Is.True,
            "the input is processed with WaitCountdown suppression active");
        Assert.That(GetRuntime(engine).BarrierLatched(2.0), Is.False,
            "a countdown-suppressed input must not install a V1 barrier");
        Hit(engine, 6.0, DrumsAction.GreenDrum);
        Hit(engine, 6.1, DrumsAction.GreenDrum);
        engine.Update(6.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3),
                "normal lane candidate entry and continuation remain available after countdown");
            Assert.That(notes.Notes.All(note => note.WasHit), Is.True);
            Assert.That(GetRuntime(engine).BarrierLatched(6.5), Is.False);
        }
    }

    [Test]
    public void AuthoredLanes_LapsedCadenceStrikeOnActivePadRefreshesWithoutOverhit()
    {
        // A matching active-pad strike after hit forgiveness lapses is still
        // protected, as on native lanes; it refreshes cadence without scoring.
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.1, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(2.0, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(2.1, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(3.0, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(3.1, FourLaneDrumPad.BlueDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 2, 4 }, new[] { 1, 3, 5 } });

        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        Hit(engine, 1.1, DrumsAction.BlueDrum);
        Hit(engine, 1.5, DrumsAction.GreenDrum);
        Hit(engine, 2.0, DrumsAction.GreenDrum);
        Hit(engine, 2.1, DrumsAction.BlueDrum);
        Hit(engine, 3.0, DrumsAction.GreenDrum);
        Hit(engine, 3.1, DrumsAction.BlueDrum);
        engine.Update(3.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero,
                "a matching active lane pad stays protected after hit forgiveness lapses");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(6));
            Assert.That(notes.Notes.All(note => note.WasHit), Is.True,
                "later real inputs still hit normally after the fault");
        }
    }

    [Test]
    public void ActiveAuthoredPadAfterHitWindowLapses_RefreshesWithoutScoringOrOverhit()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(2.0, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(3.0, FourLaneDrumPad.GreenDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 1, 2 } });
        var protectedStrikes = 0;
        var overhits = 0;
        engine.OnOverhit += () => overhits++;
        engine.OnPadHit += (_, hit, _, inLane, _, _) =>
        {
            if (!hit && inLane) protectedStrikes++;
        };

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        Hit(engine, 1.5, DrumsAction.GreenDrum); // timer expired, but lane is still active
        Assert.That(protectedStrikes, Is.EqualTo(1));
        Assert.That(overhits, Is.Zero);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Assert.That(notes.Notes[1].WasHit, Is.False);

        Hit(engine, 1.51, DrumsAction.RedDrum);
        Assert.That(overhits, Is.EqualTo(1), "a pad outside the authored lane remains a fault");
    }

    [Test]
    public void FourAuthoredLanes_AggregateCadenceProtectsFastRollWithoutSamePadCandidate()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.00, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.05, FourLaneDrumPad.RedDrum, start: true),
            NoteSpec.Lane(1.10, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(1.15, FourLaneDrumPad.YellowDrum, start: true),
            NoteSpec.Lane(9.00, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(9.05, FourLaneDrumPad.RedDrum),
            NoteSpec.Lane(9.10, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(9.15, FourLaneDrumPad.YellowDrum),
            NoteSpec.Lane(10.00, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(10.05, FourLaneDrumPad.RedDrum, end: true),
            NoteSpec.Lane(10.10, FourLaneDrumPad.BlueDrum, end: true),
            NoteSpec.Lane(10.15, FourLaneDrumPad.YellowDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 4, 8 }, new[] { 1, 5, 9 }, new[] { 2, 6, 10 }, new[] { 3, 7, 11 } });
        var eventFlags = new List<(bool WasHit, bool WasOverhitInLane)>();
        engine.OnPadHit += (_, wasHit, _, wasOverhitInLane, _, _) =>
            eventFlags.Add((wasHit, wasOverhitInLane));
        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.00, DrumsAction.GreenDrum);
        Hit(engine, 1.05, DrumsAction.RedDrum);
        Hit(engine, 1.10, DrumsAction.BlueDrum);
        Hit(engine, 1.15, DrumsAction.YellowDrum);
        Hit(engine, 1.20, DrumsAction.GreenDrum); // 0.20 after G, 0.05 after Y; no G candidate

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4));
            Assert.That(notes.Notes.Take(4).All(note => note.WasHit), Is.True);
            Assert.That(notes.Notes.Skip(4).All(note => !note.WasHit && !note.WasMissed), Is.True,
                "protected input does not adjudicate or advance a future physical note");
            Assert.That(eventFlags[^1], Is.EqualTo((false, true)),
                "protected feedback is non-hit and carries the lane-protection signal");
        }
    }

    [Test]
    public void AuthoredLane_FinalMemberHitStopsItsProtectionImmediately()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.At(1.00, FourLaneDrumPad.GreenDrum),
            NoteSpec.At(1.05, FourLaneDrumPad.BlueDrum),
            NoteSpec.At(1.10, FourLaneDrumPad.GreenDrum),
            NoteSpec.At(1.20, FourLaneDrumPad.GreenDrum),
            NoteSpec.At(5.00, FourLaneDrumPad.BlueDrum),
            NoteSpec.At(5.10, FourLaneDrumPad.BlueDrum),
        }, authoredLanes: new[] { new[] { 0, 2, 3 }, new[] { 1, 4, 5 } });
        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.00, DrumsAction.GreenDrum);
        Hit(engine, 1.05, DrumsAction.BlueDrum);
        Hit(engine, 1.10, DrumsAction.BlueDrum);
        Hit(engine, 1.10, DrumsAction.GreenDrum);
        Hit(engine, 1.20, DrumsAction.GreenDrum);
        Hit(engine, 1.21, DrumsAction.GreenDrum); // post-end proximity grace: no overhit
        Hit(engine, 1.24, DrumsAction.BlueDrum); // active blue lane protects/refreshes independently
        Hit(engine, 1.46, DrumsAction.GreenDrum); // strict 0.25s proximity window has elapsed: overhit

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.EqualTo(1), "only the strike outside native proximity leniency is a fault");
            Assert.That(notes.Notes[4].WasHit, Is.False, "the still-active blue lane's future member is untouched");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4));
        }
    }

    [Test]
    public void AuthoredLane_ZeroProtectionWindowFallsThroughToOverhit()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.00, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(2.00, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(3.00, FourLaneDrumPad.GreenDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 1, 2 } }, laneAutohitWindow: 0);
        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, 1.00, DrumsAction.GreenDrum);
        Assert.DoesNotThrow(() => Hit(engine, 1.20, DrumsAction.RedDrum));
        engine.Update(1.3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.EqualTo(1));
            Assert.That(notes.Notes[0].WasHit, Is.True);
            Assert.That(notes.Notes.Skip(1).All(note => !note.WasHit), Is.True);
        }
    }

    [Test]
    public void SingleAuthoredLane_LapsedSamePadIsProtectedLikeNativeLane()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(2.0, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(3.0, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.At(4.0, FourLaneDrumPad.BlueDrum),
        }, authoredLanes: new[] { new[] { 0, 1, 2 } });
        int overhitEvents = 0;
        int unmatchedFeedback = 0;
        engine.OnOverhit += () => overhitEvents++;
        engine.OnPadHit += (_, wasHit, _, wasOverhitInLane, _, _) =>
        {
            if (!wasHit && wasOverhitInLane) unmatchedFeedback++;
        };

        Hit(engine, 1.0, DrumsAction.GreenDrum);
        Hit(engine, 1.5, DrumsAction.GreenDrum); // no candidate; cadence has lapsed

        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.EngineStats.Overhits, Is.Zero,
                "an active matching pad must not overhit just because hit forgiveness lapsed");
            Assert.That(overhitEvents, Is.Zero);
            Assert.That(unmatchedFeedback, Is.EqualTo(1), "pad feedback retains the lane indication");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1), "the unmatched input scores no note");
            Assert.That(GetRuntime(engine).BarrierLatched(1.5), Is.False,
                "protected lane input does not latch an off-lane fault barrier");
            Assert.That(notes.Notes.Skip(1).All(note => !note.WasHit), Is.True,
                "the unmatched input does not adjudicate later physical notes");
        }

        Hit(engine, 2.0, DrumsAction.GreenDrum);
        Assert.That(notes.Notes[1].WasHit, Is.True,
            "a later real candidate still hits normally after protected lane input");
    }

    [Test]
    public void AuthoredLane_NegativeInputTimestampsRetainCadencePresence()
    {
        var (engine, notes) = Build(new[]
        {
            NoteSpec.Lane(-1.00, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(-0.50, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(-0.25, FourLaneDrumPad.GreenDrum, end: true),
        }, authoredLanes: new[] { new[] { 0, 1, 2 } });
        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        Hit(engine, -1.00, DrumsAction.GreenDrum);
        Hit(engine, -0.90, DrumsAction.GreenDrum);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero, "negative-time real input refreshes protection normally");
            Assert.That(notes.Notes[0].WasHit, Is.True);
            Assert.That(notes.Notes.Skip(1).All(note => !note.WasHit), Is.True);
        }
    }

    [Test]
    public void ThreeAuthoredLanes_GBY_GBYRepeating_FastNonliteralSchedule()
    {
        // Literal GBGY-repeating chart (G B G Y / B G Y G / B Y by pad cycles), one
        // authored lane per pad. The input schedule is fast and nonliteral: each pad
        // is struck every ~0.12s, offset from the chart so most strikes are protected
        // no-ops between candidate windows, and the few inside a window land as real
        // early hits. Every strike is either a real hit or a protected no-op.
        var specs = new List<NoteSpec>
        {
            NoteSpec.Lane(1.0, FourLaneDrumPad.GreenDrum, start: true),
            NoteSpec.Lane(1.4, FourLaneDrumPad.BlueDrum, start: true),
            NoteSpec.Lane(1.8, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(2.2, FourLaneDrumPad.YellowDrum, start: true),
            NoteSpec.Lane(2.6, FourLaneDrumPad.BlueDrum),
            NoteSpec.Lane(3.0, FourLaneDrumPad.GreenDrum),
            NoteSpec.Lane(3.4, FourLaneDrumPad.YellowDrum),
            NoteSpec.Lane(3.8, FourLaneDrumPad.GreenDrum, end: true),
            NoteSpec.Lane(4.2, FourLaneDrumPad.BlueDrum, end: true),
            NoteSpec.Lane(4.6, FourLaneDrumPad.YellowDrum, end: true),
        };
        var (engine, notes) = Build(specs, authoredLanes: new[]
        {
            new[] { 0, 2, 5, 7 },   // green
            new[] { 1, 4, 8 },      // blue
            new[] { 3, 6, 9 },      // yellow
        });

        int overhits = 0;
        engine.OnOverhit += () => overhits++;

        // Full input schedule, merged and time-sorted: the exact lane entries plus a
        // fast nonliteral but lane-valid roll — green from 1.12, blue from 1.52, yellow
        // from 2.32, each stepped 0.12s and clipped to its lane's temporal end. Most
        // strikes are protected no-ops between candidate windows; the few inside a
        // window land as real (often early) hits.
        var schedule = new List<(double Time, DrumsAction Action)>
        {
            (1.0, DrumsAction.GreenDrum),
            (1.4, DrumsAction.BlueDrum),
            (2.2, DrumsAction.YellowDrum),
        };
        AddStrikes(schedule, DrumsAction.GreenDrum, 1.12, 0.12, 3.88);
        AddStrikes(schedule, DrumsAction.BlueDrum, 1.52, 0.12, 4.28);
        AddStrikes(schedule, DrumsAction.YellowDrum, 2.32, 0.12, 4.68);
        foreach (var strike in schedule.OrderBy(strike => strike.Time))
        {
            Hit(engine, strike.Time, strike.Action);
        }

        engine.Update(5.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(overhits, Is.Zero,
                "no active-lane overhits: every strike is a real hit or a protected no-op");
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(10),
                "all ten physical notes are hit by real input; none is auto-hit");
            Assert.That(notes.Notes.All(note => note.WasHit), Is.True);
        }
    }

    [Test]
    public void FourAuthoredLanes_GRBY_NominalAndDistortedRapidSchedules()
    {
        // 24 notes cycling Green/Red/Blue/Yellow every 0.08s (0.32s same-pad spacing),
        // one authored lane per pad: arbitrary simultaneous lane support. Candidate
        // windows are [T-0.07, T+0.10].
        var pads = new[]
        {
            FourLaneDrumPad.GreenDrum, FourLaneDrumPad.RedDrum,
            FourLaneDrumPad.BlueDrum, FourLaneDrumPad.YellowDrum,
        };
        var actions = new[]
        {
            DrumsAction.GreenDrum, DrumsAction.RedDrum,
            DrumsAction.BlueDrum, DrumsAction.YellowDrum,
        };
        var specs = new List<NoteSpec>();
        var laneMembers = new List<List<int>>();
        for (var p = 0; p < 4; p++) laneMembers.Add(new List<int>());
        for (var i = 0; i < 24; i++)
        {
            var time = Math.Round(1.0 + 0.08 * i, 3);
            specs.Add(NoteSpec.Lane(time, pads[i % 4],
                start: i < 4, end: i >= 20));
            laneMembers[i % 4].Add(i);
        }

        // Control 1: entries only, then silence — exact physical hits stay input-driven.
        var silent = Build(specs, authoredLanes: laneMembers.Select(m => m.ToArray()).ToArray());
        int silentOverhits = 0;
        silent.Engine.OnOverhit += () => silentOverhits++;
        Hit(silent.Engine, 1.00, DrumsAction.GreenDrum);
        Hit(silent.Engine, 1.08, DrumsAction.RedDrum);
        Hit(silent.Engine, 1.16, DrumsAction.BlueDrum);
        Hit(silent.Engine, 1.24, DrumsAction.YellowDrum);
        silent.Engine.Update(4.0);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(silentOverhits, Is.Zero);
            Assert.That(silent.Engine.EngineStats.NotesHit, Is.EqualTo(4),
                "no unprompted scoring: only the four real entries hit");
            Assert.That(silent.Notes.Notes.Skip(4).All(note => note.WasMissed), Is.True);
        }

        // Control 2: nominal GRBY repeating schedule — every note hit exactly, in order.
        var nominal = Build(specs, authoredLanes: laneMembers.Select(m => m.ToArray()).ToArray());
        int nominalOverhits = 0;
        nominal.Engine.OnOverhit += () => nominalOverhits++;
        for (var i = 0; i < 24; i++)
        {
            Hit(nominal.Engine, Math.Round(1.0 + 0.08 * i, 3), actions[i % 4]);
        }
        nominal.Engine.Update(4.0);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nominalOverhits, Is.Zero);
            Assert.That(nominal.Engine.EngineStats.NotesHit, Is.EqualTo(24));
            Assert.That(nominal.Notes.Notes.All(note => note.WasHit), Is.True);
        }

        // Distorted rapid schedule GRBYGGRRBBYYGYGBRBYG at 0.017s steps from 1.26,
        // preceded by one in-lane filler strike per pad so every lane's cadence is
        // freshly refreshed when the distorted roll begins. The entire input stream
        // (entries, fillers, distorted roll, nominal finish) is merged and time-sorted
        // before submission. Each strike lands either as a real candidate hit or as a
        // protected no-op — never as a fault.
        var distorted = Build(specs, authoredLanes: laneMembers.Select(m => m.ToArray()).ToArray());
        var strikeLog = new List<(double Time, int Pad)>();
        int distortedOverhits = 0;
        distorted.Engine.OnOverhit += () => distortedOverhits++;

        const string pattern = "GRBYGGRRBBYYGYGBRBYG";
        var actionByChar = new Dictionary<char, DrumsAction>
        {
            ['G'] = DrumsAction.GreenDrum,
            ['R'] = DrumsAction.RedDrum,
            ['B'] = DrumsAction.BlueDrum,
            ['Y'] = DrumsAction.YellowDrum,
        };
        var padByAction = new Dictionary<DrumsAction, int>
        {
            [DrumsAction.GreenDrum] = (int) FourLaneDrumPad.GreenDrum,
            [DrumsAction.RedDrum] = (int) FourLaneDrumPad.RedDrum,
            [DrumsAction.BlueDrum] = (int) FourLaneDrumPad.BlueDrum,
            [DrumsAction.YellowDrum] = (int) FourLaneDrumPad.YellowDrum,
        };
        var schedule = new List<(double Time, DrumsAction Action)>
        {
            (1.00, DrumsAction.GreenDrum),
            (1.08, DrumsAction.RedDrum),
            (1.14, DrumsAction.GreenDrum), // filler: refresh green cadence
            (1.16, DrumsAction.BlueDrum),
            (1.22, DrumsAction.RedDrum),   // filler: refresh red cadence
            (1.23, DrumsAction.BlueDrum),  // filler: refresh blue cadence
            (1.24, DrumsAction.YellowDrum),
        };
        for (var i = 0; i < pattern.Length; i++)
        {
            schedule.Add((Math.Round(1.26 + 0.017 * i, 4), actionByChar[pattern[i]]));
        }

        // Nominal finish for the remaining notes.
        for (var i = 9; i < 24; i++)
        {
            schedule.Add((Math.Round(1.0 + 0.08 * i, 3), actions[i % 4]));
        }

        foreach (var strike in schedule.OrderBy(strike => strike.Time).ThenBy(strike => strike.Action))
        {
            Hit(distorted.Engine, strike.Time, strike.Action);
            strikeLog.Add((strike.Time, padByAction[strike.Action]));
        }
        distorted.Engine.Update(4.0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(distortedOverhits, Is.Zero,
                "zero protected-lane overhits across the distorted rapid schedule");
            Assert.That(distorted.Engine.EngineStats.NotesHit, Is.EqualTo(24));
            Assert.That(distorted.Notes.Notes.All(note => note.WasHit), Is.True);
            // Every hit note was struck on its pad within its frozen window: hits stay
            // input-driven even under the distorted schedule.
            foreach (var note in distorted.Notes.Notes)
            {
                Assert.That(
                    strikeLog.Any(strike => strike.Pad == note.Pad
                        && strike.Time >= note.Time - 0.07 && strike.Time <= note.Time + 0.10),
                    Is.True, $"note at {note.Time} must be covered by a real strike");
            }
        }

        // Off-lane fault control: kick belongs to no authored lane, so a kick strike
        // with no candidate is a genuine fault (native overhit behavior is unchanged).
        var fault = Build(specs, authoredLanes: laneMembers.Select(m => m.ToArray()).ToArray());
        int faultOverhits = 0;
        fault.Engine.OnOverhit += () => faultOverhits++;
        Hit(fault.Engine, 1.00, DrumsAction.GreenDrum);
        Hit(fault.Engine, 1.08, DrumsAction.RedDrum);
        Hit(fault.Engine, 1.16, DrumsAction.BlueDrum);
        Hit(fault.Engine, 1.24, DrumsAction.YellowDrum);
        Hit(fault.Engine, 1.30, DrumsAction.Kick);
        fault.Engine.Update(4.0);
        Assert.That(faultOverhits, Is.EqualTo(1),
            "an off-lane pad remains a normal native overhit fault");
    }

    private static void AddStrikes(List<(double Time, DrumsAction Action)> strikes,
        DrumsAction action, double from, double step, double until)
    {
        for (var t = from; t <= until + 1e-9; t += step)
        {
            strikes.Add((Math.Round(t, 3), action));
        }
    }

    private static EliteFillRuntimeScheduler GetRuntime(YargDrumsEngine engine)
    {
        var field = typeof(DrumsEngine).GetField("_eliteFillRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
        return (EliteFillRuntimeScheduler) (field?.GetValue(engine)
            ?? throw new AssertionException("Elite Fill V1 runtime was not initialized."));
    }

    private static (YargDrumsEngine Engine, InstrumentDifficulty<DrumNote> Notes) Build(
        IReadOnlyList<NoteSpec> specs, double? codaStart = null, double? codaEnd = null, bool isBot = false,
        IReadOnlyList<int[]>? authoredLanes = null, IReadOnlyList<int[]>? unresolvedLanes = null,
        double laneAutohitWindow = 0.16)
    {
        var notes = new List<DrumNote>();
        var origins = new List<EliteDrumConversionOrigin?>();
        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var source = new EliteDrumSourceDefinition($"fixture-{i}", i, (int) spec.Pad,
                (uint) Math.Round(spec.Time / Beat * Resolution), 0, spec.Time);
            var origin = new EliteDrumConversionOrigin(source);
            var laneFlags = spec.IsLane ? NoteFlags.Tremolo : NoteFlags.None;
            if (spec.LaneStart) laneFlags |= NoteFlags.LaneStart;
            if (spec.LaneEnd) laneFlags |= NoteFlags.LaneEnd;
            var note = new DrumNote(spec.Pad, spec.Type, spec.DrumFlags, laneFlags,
                spec.Time, source.StartTick, false, DrumStem.Else, origin);
            foreach (var childPad in spec.ChildPads)
            {
                var childSource = new EliteDrumSourceDefinition($"fixture-{i}-child-{childPad}",
                    i + 100, childPad, source.StartTick, 0, spec.Time);
                note.AddChildNote(new DrumNote(childPad, spec.Type, DrumNoteFlags.None, NoteFlags.None,
                    spec.Time, source.StartTick, false, DrumStem.Else, new EliteDrumConversionOrigin(childSource)));
            }
            notes.Add(note);
            origins.Add(origin);
        }
        for (int i = 1; i < notes.Count; i++)
        {
            notes[i - 1].NextNote = notes[i];
            notes[i].PreviousNote = notes[i - 1];
        }

        var phrases = new List<Phrase>();
        if (codaStart.HasValue && codaEnd.HasValue)
        {
            uint startTick = (uint) Math.Round(codaStart.Value / Beat * Resolution);
            uint endTick = (uint) Math.Round(codaEnd.Value / Beat * Resolution);
            phrases.Add(new Phrase(PhraseType.BigRockEnding, codaStart.Value, codaEnd.Value - codaStart.Value,
                startTick, endTick - startTick));
        }
        var diff = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert, notes, phrases, new List<TextEvent>());
        var records = new List<EliteDrumVisualDescriptorV1>();
        if (authoredLanes is not null)
        {
            for (var i = 0; i < authoredLanes.Count; i++)
            {
                var laneOrigins = authoredLanes[i]
                    .Select(index => origins[index])
                    .ToArray();
                if (laneOrigins.Any(origin => origin is null)) continue;
                records.Add(MakeAuthoredLaneRecord($"fixture-lane-{i}", laneOrigins!));
            }
        }

        // Unresolved records keep their provenance (mixed final pads and similar
        // descriptor failures) exactly like the loader publishes them, while the
        // generic lane flags above stay stamped on the notes.
        if (unresolvedLanes is not null)
        {
            for (var i = 0; i < unresolvedLanes.Count; i++)
            {
                var laneOrigins = unresolvedLanes[i]
                    .Select(index => origins[index])
                    .ToArray();
                if (laneOrigins.Any(origin => origin is null)) continue;
                records.Add(MakeAuthoredLaneRecord($"fixture-unresolved-{i}", laneOrigins!, resolved: false));
            }
        }

        if (records.Count > 0)
        {
            diff.SetEliteDrumAuthoredLanePhraseRecords(records);
        }
        return (CreateEngine(diff, isBot, laneAutohitWindow), diff);
    }

    private static EliteDrumVisualDescriptorV1 MakeAuthoredLaneRecord(string id,
        IReadOnlyList<EliteDrumConversionOrigin> laneOrigins, bool resolved = true)
    {
        var finalPad = resolved
            ? new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int) FourLaneDrumPad.GreenDrum)
            : EliteDrumFinalPadIdentity.UnresolvedFor(Instrument.ProDrums);
        return new EliteDrumVisualDescriptorV1(id, laneOrigins.Select(origin => origin.Source.SourceId),
            laneOrigins, finalPad, new EliteDrumComponentVisualDescriptor(id, true, id, 0, "ProDrums",
                Array.Empty<EliteDrumComponentMetadata>()),
            0, 0, 0, 0, 0, 0, true, false);
    }

    private static YargDrumsEngine CreateEngine(InstrumentDifficulty<DrumNote> diff, bool isBot,
        double laneAutohitWindow = 0.16)
    {
        var hitWindow = new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, laneAutohitWindow, 0.25);
        var parameters = new DrumsEngineParameters(hitWindow, 4, StarThresholds, SoloThresholds,
            DrumsEngineParameters.DrumMode.ProFourLane, false, true,
            DrumsEngineParameters.ELITE_FILL_RULESET_V1, 1f);
        return new YargDrumsEngine(diff, CreateSyncTrack(), parameters, isBot, false);
    }

    private static SyncTrack CreateSyncTrack()
    {
        var sync = new SyncTrack(Resolution);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        return sync;
    }

    private static void Hit(YargDrumsEngine engine, double time, DrumsAction action, float velocity = 1f)
    {
        var input = GameInput.Create(time, action, velocity);
        engine.QueueInput(ref input);
        engine.Update(time);
    }

    private sealed class NoteSpec
    {
        public double Time { get; }
        public FourLaneDrumPad Pad { get; }
        public DrumNoteType Type { get; }
        public DrumNoteFlags DrumFlags { get; set; }
        public List<int> ChildPads { get; } = new();

        private NoteSpec(double time, FourLaneDrumPad pad, DrumNoteType type)
        {
            Time = time;
            Pad = pad;
            Type = type;
        }

        public static NoteSpec At(double time, FourLaneDrumPad pad, DrumNoteType type = DrumNoteType.Neutral)
            => new(time, pad, type);

        public static NoteSpec Lane(double time, FourLaneDrumPad pad, bool start = false, bool end = false)
        {
            var spec = At(time, pad);
            spec._laneMember = true;
            spec.LaneStart = start;
            spec.LaneEnd = end;
            return spec;
        }

        public bool LaneStart { get; private set; }
        public bool LaneEnd { get; private set; }
        public bool IsLane => LaneStart || LaneEnd || _laneMember;
        private bool _laneMember;
    }
}
