using System;
using System.Collections.Generic;
using System.Linq;
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

    private static (YargDrumsEngine Engine, InstrumentDifficulty<DrumNote> Notes) Build(
        IReadOnlyList<NoteSpec> specs, double? codaStart = null, double? codaEnd = null, bool isBot = false)
    {
        var notes = new List<DrumNote>();
        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var source = new EliteDrumSourceDefinition($"fixture-{i}", i, (int) spec.Pad,
                (uint) Math.Round(spec.Time / Beat * Resolution), 0, spec.Time);
            var origin = new EliteDrumConversionOrigin(source);
            var laneFlags = spec.IsLane ? NoteFlags.Tremolo : NoteFlags.None;
            if (spec.LaneStart) laneFlags |= NoteFlags.LaneStart;
            if (spec.LaneEnd) laneFlags |= NoteFlags.LaneEnd;
            var note = new DrumNote(spec.Pad, spec.Type, DrumNoteFlags.None, laneFlags,
                spec.Time, source.StartTick, false, DrumStem.Else, origin);
            foreach (var childPad in spec.ChildPads)
            {
                var childSource = new EliteDrumSourceDefinition($"fixture-{i}-child-{childPad}",
                    i + 100, childPad, source.StartTick, 0, spec.Time);
                note.AddChildNote(new DrumNote(childPad, spec.Type, DrumNoteFlags.None, NoteFlags.None,
                    spec.Time, source.StartTick, false, DrumStem.Else, new EliteDrumConversionOrigin(childSource)));
            }
            notes.Add(note);
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
        return (CreateEngine(diff, isBot), diff);
    }

    private static YargDrumsEngine CreateEngine(InstrumentDifficulty<DrumNote> diff, bool isBot)
    {
        var hitWindow = new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, 0.16, 0.25);
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
