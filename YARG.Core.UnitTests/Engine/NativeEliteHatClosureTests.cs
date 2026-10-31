// pattern: Imperative Shell
using System;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Engine;

public sealed class NativeEliteHatClosureTests
{
    private static EliteDrumNote Note(EliteDrumPad pad, double time,
        EliteDrumsHatState state = EliteDrumsHatState.Indifferent) =>
        new(pad, DrumNoteType.Neutral, state, EliteDrumsHatPedalType.Stomp, false,
            DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, (uint)(time * 960), false);

    private static EliteDrumsEngine Create(bool enabled, params EliteDrumNote[] targets)
        => Create(enabled, false, false, targets);

    private static EliteDrumsEngine Create(bool enabled, bool autoPedal, bool bot, params EliteDrumNote[] targets)
    {
        var notes = new[] { Note(EliteDrumPad.Snare, .5) }.Concat(targets)
            .Append(Note(EliteDrumPad.Ride, 2)).ToList();
        for (int i = 1; i < notes.Count; i++)
        {
            notes[i - 1].NextNote = notes[i];
            notes[i].PreviousNote = notes[i - 1];
        }
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var preset = EnginePreset.Default.Drums.Create([1, 2, 3, 4, 5, 6],
            [1, 2, 3, 4, 5, 6], DrumsEngineParameters.DrumMode.ProFourLane);
        var parameters = new DrumsEngineParameters(preset.HitWindow, preset.MaxMultiplier,
            preset.StarMultiplierThresholds, preset.SoloBonusStarMultiplierThresholds, preset.Mode,
            preset.NoStarPowerOverlap, preset.EnableLanes, hiHatClosureSuppression: enabled);
        var engine = new EliteDrumsEngine(new(Instrument.EliteDrums, Difficulty.Expert, notes, new(), new()),
            sync, parameters, bot, true, autoPedal);
        engine.Update(-1);
        if (!bot) Queue(engine, .5, EliteDrumsAction.EliteSnare);
        engine.Update(.6);
        return engine;
    }

    private static void Queue(EliteDrumsEngine engine, double time, EliteDrumsAction action, float value = .8f)
    {
        var input = GameInput.Create(time, action, value);
        engine.QueueInput(ref input);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChartedStompForgivesCoincidentClosedHat(bool hatFirst)
    {
        var stomp = Note(EliteDrumPad.HatPedal, 1);
        var engine = Create(true, stomp);
        Queue(engine, 1, hatFirst ? EliteDrumsAction.EliteClosedHiHat : EliteDrumsAction.EliteStomp);
        Queue(engine, 1.015, hatFirst ? EliteDrumsAction.EliteStomp : EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.1);
        Assert.Multiple(() =>
        {
            Assert.That(stomp.WasHit, Is.True);
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.Combo, Is.EqualTo(2));
        });
    }

    [Test]
    public void DisabledRetainsOrdinaryClosedHatOverhit()
    {
        var engine = Create(false, Note(EliteDrumPad.HatPedal, 1));
        Queue(engine, 1, EliteDrumsAction.EliteStomp);
        Queue(engine, 1.015, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.1);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(engine.EngineStats.OverhitsByAction[(int)EliteDrumsAction.EliteClosedHiHat], Is.EqualTo(1));
    }

    [TestCase(false, EliteDrumsHatState.Closed)]
    [TestCase(true, EliteDrumsHatState.Closed)]
    [TestCase(false, EliteDrumsHatState.Indifferent)]
    [TestCase(true, EliteDrumsHatState.Indifferent)]
    public void CymbalTargetStillScores(bool hatFirst, EliteDrumsHatState state)
    {
        var hat = Note(EliteDrumPad.HiHat, 1, state);
        var engine = Create(true, hat);
        Queue(engine, 1, hatFirst ? EliteDrumsAction.EliteClosedHiHat : EliteDrumsAction.EliteStomp);
        engine.Update(1);
        Queue(engine, 1.015, hatFirst ? EliteDrumsAction.EliteStomp : EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.1);
        Assert.That(hat.WasHit, Is.True);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OpenOnlyAttributesOneFaultToPedal(bool hatFirst)
    {
        var hat = Note(EliteDrumPad.HiHat, 1, EliteDrumsHatState.Open);
        var engine = Create(true, hat);
        Queue(engine, 1, hatFirst ? EliteDrumsAction.EliteClosedHiHat : EliteDrumsAction.EliteStomp);
        engine.Update(1);
        Queue(engine, 1.015, hatFirst ? EliteDrumsAction.EliteStomp : EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.1);
        Assert.That(hat.WasHit, Is.False);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(engine.EngineStats.OverhitsByAction.Keys, Is.EquivalentTo(new[] { (int)EliteDrumsAction.EliteStomp }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyHatWindowForgivesBoth(bool hatFirst)
    {
        var engine = Create(true);
        Queue(engine, 1, hatFirst ? EliteDrumsAction.EliteClosedHiHat : EliteDrumsAction.EliteStomp);
        engine.Update(1);
        Queue(engine, 1.015, hatFirst ? EliteDrumsAction.EliteStomp : EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.1);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        Assert.That(engine.EngineStats.Combo, Is.EqualTo(1));
    }

    [TestCase(false, 0d, 0)]
    [TestCase(true, 0d, 0)]
    [TestCase(false, .030, 0)]
    [TestCase(true, .030, 0)]
    [TestCase(false, .030001, 1)]
    [TestCase(true, .030001, 1)]
    public void PairingWindowIncludesBoundaryButNotOutside(bool hatFirst, double gap, int overhits)
    {
        var engine = Create(true, Note(EliteDrumPad.HatPedal, 1));
        Queue(engine, 1, hatFirst ? EliteDrumsAction.EliteClosedHiHat : EliteDrumsAction.EliteStomp);
        engine.Update(1);
        Queue(engine, 1 + gap, hatFirst ? EliteDrumsAction.EliteStomp : EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.2);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(overhits));
    }

    [Test]
    public void CoincidentChartedStompAndCymbalBothScore()
    {
        var stomp = Note(EliteDrumPad.HatPedal, 1);
        var hat = Note(EliteDrumPad.HiHat, 1);
        stomp.AddChildNote(hat);
        var engine = Create(true, stomp);
        Queue(engine, 1, EliteDrumsAction.EliteClosedHiHat);
        Queue(engine, 1.01, EliteDrumsAction.EliteStomp);
        engine.Update(1.1);
        Assert.That(stomp.WasHit && hat.WasHit, Is.True);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OnePedalCannotSwallowTwoIdenticalHandStrikes(bool hatFirst)
    {
        var engine = Create(true);
        int pairs = 0;
        engine.OnHiHatClosurePair += _ => pairs++;
        if (!hatFirst) Queue(engine, 1, EliteDrumsAction.EliteStomp);
        Queue(engine, 1, EliteDrumsAction.EliteClosedHiHat);
        Queue(engine, 1, EliteDrumsAction.EliteClosedHiHat);
        if (hatFirst) Queue(engine, 1, EliteDrumsAction.EliteStomp);
        engine.Update(1.1);
        Assert.That(pairs, Is.EqualTo(1));
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
        Assert.That(engine.EngineStats.OverhitsByAction[(int)EliteDrumsAction.EliteClosedHiHat], Is.EqualTo(1));
    }

    [TestCase(0f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void ReleaseOrInvalidValueCannotBecomePartner(float value)
    {
        var engine = Create(true);
        int pairs = 0;
        engine.OnHiHatClosurePair += _ => pairs++;
        Queue(engine, 1, EliteDrumsAction.EliteStomp, value);
        Queue(engine, 1.01, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.1);
        Assert.That(pairs, Is.Zero);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
    }

    [TestCase(EliteDrumsAction.EliteSplash)]
    [TestCase(EliteDrumsAction.EliteSnare)]
    public void OtherActionCannotBecomeClosurePartner(EliteDrumsAction action)
    {
        var engine = Create(true);
        int pairs = 0;
        engine.OnHiHatClosurePair += _ => pairs++;
        Queue(engine, 1, action);
        Queue(engine, 1.01, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.1);
        Assert.That(pairs, Is.Zero);
        Assert.That(engine.EngineStats.OverhitsByAction.ContainsKey((int)EliteDrumsAction.EliteClosedHiHat), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PairObservationRetainsOriginalTimesAndValues(bool booleanPedal)
    {
        var engine = Create(true);
        HiHatClosureObservation? observed = null;
        engine.OnHiHatClosurePair += sample => observed = sample;
        Queue(engine, 1, EliteDrumsAction.EliteClosedHiHat, .42f);
        var pedal = booleanPedal ? GameInput.Create(1.012, EliteDrumsAction.EliteStomp, true)
            : GameInput.Create(1.012, EliteDrumsAction.EliteStomp, .27f);
        engine.QueueInput(ref pedal);
        engine.Update(1.1);
        Assert.That(observed.HasValue, Is.True);
        var sample = observed.Value;
        Assert.That(sample.PedalInput.Time, Is.EqualTo(1.012));
        Assert.That(sample.ClosedHatInput.Time, Is.EqualTo(1));
        Assert.That(sample.TimeDelta, Is.EqualTo(-.012).Within(1e-12));
        Assert.That(sample.PedalInput.Integer, Is.EqualTo(pedal.Integer));
        Assert.That(sample.PedalValue, Is.EqualTo(booleanPedal ? null : (float?).27f));
        Assert.That(sample.ClosedHatValue, Is.EqualTo(.42f));
        Assert.That(sample.Disposition, Is.EqualTo(HiHatClosureDisposition.Forgiven));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisabledHasNoDelayOrObservationAndEnabledKeepsJudgingTime(bool enabled)
    {
        var hat = Note(EliteDrumPad.HiHat, 1);
        var engine = Create(enabled, hat);
        double judged = -1;
        engine.OnPadHit += (_, hit, _, _, _, _) => { if (hit) judged = engine.CurrentTime; };
        Queue(engine, 1, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1);
        Assert.That(hat.WasHit, Is.EqualTo(!enabled));
        engine.Update(1.1);
        Assert.That(hat.WasHit, Is.True);
        Assert.That(judged, Is.EqualTo(1));
        Assert.That(engine.InputProcessingDelay, Is.EqualTo(enabled ? .03 : 0));
    }

    [TestCase(.5)]
    [TestCase(1)]
    [TestCase(1.5)]
    [TestCase(3)]
    public void PhysicalPairingWindowFollowsSongSpeed(double speed)
    {
        var engine = Create(true);
        engine.SetSpeed(speed);
        Queue(engine, 1, EliteDrumsAction.EliteStomp);
        engine.Update(1);
        Queue(engine, 1 + .025 * speed, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1 + .2 * speed);
        Assert.That(engine.InputProcessingDelay, Is.EqualTo(.03 * speed));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [Test]
    public void SpeedChangeCannotMoveJudgingClockBackwards()
    {
        var engine = Create(true);
        engine.SetSpeed(1);
        engine.Update(1);
        double previous = engine.CurrentTime;
        engine.SetSpeed(3);
        engine.Update(1.001);
        Assert.That(engine.CurrentTime, Is.GreaterThanOrEqualTo(previous));
    }

    [Test]
    public void ResetAndSeekRebuildPendingPairFromOriginalInputs()
    {
        var stomp = Note(EliteDrumPad.HatPedal, 1);
        var engine = Create(true, stomp);
        var inputs = new[] { GameInput.Create(.5, EliteDrumsAction.EliteSnare, .8f),
            GameInput.Create(1, EliteDrumsAction.EliteClosedHiHat, .4f),
            GameInput.Create(1.02, EliteDrumsAction.EliteStomp, .7f) };
        for (int run = 0; run < 2; run++)
        {
            Assert.That(engine.ProcessUpToTime(1.04, inputs), Is.EqualTo(3));
            engine.Update(1.2);
            Assert.That(stomp.WasHit, Is.True);
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        }
    }

    [Test]
    public void RandomFrameBatchingKeepsPairingAndFaultAttribution()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var engine = Create(true, Note(EliteDrumPad.HiHat, 1, EliteDrumsHatState.Open));
            var random = new Random(seed);
            var inputs = new[] { GameInput.Create(1, EliteDrumsAction.EliteClosedHiHat, .3f),
                GameInput.Create(1.022, EliteDrumsAction.EliteStomp, .7f) };
            int next = 0;
            for (double frame = .7; frame < 1.3; frame += .001 + random.NextDouble() * .09)
            {
                while (next < inputs.Length && inputs[next].Time <= frame)
                {
                    var input = inputs[next++];
                    engine.QueueInput(ref input);
                }
                engine.Update(frame);
            }
            engine.Update(1.4);
            Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1), "seed " + seed);
            Assert.That(engine.EngineStats.OverhitsByAction.Keys,
                Is.EquivalentTo(new[] { (int)EliteDrumsAction.EliteStomp }), "seed " + seed);
        }
    }

    [Test]
    public void BeginnerWildcardCanBePlayedByPairedCymbal()
    {
        var wildcard = Note(EliteDrumPad.Wildcard, 1);
        var engine = Create(true, wildcard);
        Queue(engine, 1, EliteDrumsAction.EliteStomp);
        Queue(engine, 1.01, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.1);
        Assert.That(wildcard.WasHit, Is.True);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [Test]
    public void PairedCymbalStillCompletesPendingHiHatFlam()
    {
        var flam = new EliteDrumNote(EliteDrumPad.HiHat, DrumNoteType.Neutral,
            EliteDrumsHatState.Closed, EliteDrumsHatPedalType.Stomp, true,
            DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 1, 960, false);
        var engine = Create(true, flam);
        int followups = 0;
        engine.OnFlamFollowUp += (_, _, _, _) => followups++;
        Queue(engine, .98, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.011);
        Assert.That(flam.WasHit, Is.True);
        Queue(engine, 1.02, EliteDrumsAction.EliteStomp);
        Queue(engine, 1.03, EliteDrumsAction.EliteClosedHiHat);
        engine.Update(1.1);
        Assert.That(followups, Is.EqualTo(1));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        Assert.That(engine.EngineStats.CommittedScore, Is.EqualTo(150));
    }

    [Test]
    public void OptionalPedalAssistanceRetainsBothPhysicalTargets()
    {
        var stomp = Note(EliteDrumPad.HatPedal, 1);
        var hat = Note(EliteDrumPad.HiHat, 1);
        stomp.AddChildNote(hat);
        var engine = Create(true, true, false, stomp);
        Queue(engine, 1, EliteDrumsAction.EliteClosedHiHat);
        Queue(engine, 1, EliteDrumsAction.EliteStomp);
        engine.Update(1.1);
        Assert.That(stomp.WasHit && hat.WasHit, Is.True);
        Assert.That(engine.EngineStats.OptionalPedalHits, Is.EqualTo(1));
        Assert.That(engine.EngineStats.AssistedPedalNotes, Is.Zero);
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
    }

    [Test]
    public void BotHasNoPairingDelayOrSyntheticPairObservations()
    {
        var hat = Note(EliteDrumPad.HiHat, 1);
        var engine = Create(true, false, true, hat);
        int pairs = 0;
        engine.OnHiHatClosurePair += _ => pairs++;
        engine.Update(1);
        Assert.That(hat.WasHit, Is.True);
        Assert.That(engine.InputProcessingDelay, Is.Zero);
        Assert.That(pairs, Is.Zero);
    }
}
