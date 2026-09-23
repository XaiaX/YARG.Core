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
/// Regression coverage for signed (pre-song) timestamps in Elite Fill V1.
/// Charts shifted by a delay/offset (e.g. Foreplay/Longtime's intro fill) carry
/// legitimate negative note times, and the engine clock is negative during a
/// count-in/lead-in. <see cref="EliteFillParameterValidation.RequireTimestamp"/>
/// must therefore accept any finite signed value, while duration, window,
/// interval, step, and tolerance parameters remain strictly non-negative/positive.
/// </summary>
public sealed class EliteFillSignedTimestampV1Tests
{
    private const int Resolution = 480;
    private const double Beat = 0.5;
    private const double PreSongFillTime = -1.278;

    private static readonly float[] StarThresholds = { 0.06f, 0.12f, 0.2f, 0.45f, 0.75f, 1.09f };
    private static readonly float[] SoloThresholds = { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f };

    [Test]
    public void RequireTimestamp_AcceptsFiniteSignedValues_AndRejectsNonFinite()
    {
        Assert.DoesNotThrow(() => EliteFillParameterValidation.RequireTimestamp(PreSongFillTime));
        Assert.DoesNotThrow(() => EliteFillParameterValidation.RequireTimestamp(-1e100));
        Assert.DoesNotThrow(() => EliteFillParameterValidation.RequireTimestamp(0.0));
        Assert.DoesNotThrow(() => EliteFillParameterValidation.RequireTimestamp(2.5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => EliteFillParameterValidation.RequireTimestamp(double.NaN),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => EliteFillParameterValidation.RequireTimestamp(double.PositiveInfinity),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => EliteFillParameterValidation.RequireTimestamp(double.NegativeInfinity),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    [Test]
    public void DurationParameters_StillRejectNegativeAndNonFinite()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => EliteFillParameterValidation.RequireNonNegativeFinite(-0.001, "windowSeconds"),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => EliteFillParameterValidation.RequireNonNegativeFinite(double.NaN, "windowSeconds"),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => EliteFillParameterValidation.RequirePositiveFinite(0.0, "stepSeconds"),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new EliteFillPolicyV1(new EliteFillPolicyParameters(entryWindowSeconds: -0.050)),
                Throws.ArgumentException);
            Assert.That(() => EliteFillTimeGrouping.Group(Array.Empty<EliteFillCandidate>(), -1.0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    [Test]
    public void DeadlineAt_NegativePreSongAnchor_ProducesSignedWindows()
    {
        var parameters = new EliteFillPolicyParameters();
        var deadline = EliteFillDeadlineCalculator.Calculate(PreSongFillTime, parameters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deadline.AnchorTimestamp, Is.EqualTo(PreSongFillTime));
            Assert.That(deadline.EntryUntil, Is.EqualTo(PreSongFillTime + parameters.EntryWindowSeconds));
            Assert.That(deadline.GraceUntil, Is.EqualTo(PreSongFillTime + parameters.EntryWindowSeconds
                + parameters.GraceWindowSeconds));
            Assert.That(deadline.CommitAt, Is.EqualTo(PreSongFillTime + parameters.EntryWindowSeconds
                + parameters.GraceWindowSeconds + parameters.PendingCommitDelaySeconds));

            Assert.That(deadline.Classify(PreSongFillTime), Is.EqualTo(EliteFillWindow.Entry));
            Assert.That(deadline.Classify(PreSongFillTime + parameters.EntryWindowSeconds + 0.001),
                Is.EqualTo(EliteFillWindow.Grace));
            Assert.That(deadline.Classify(PreSongFillTime + parameters.EntryWindowSeconds
                + parameters.GraceWindowSeconds + 0.001), Is.EqualTo(EliteFillWindow.Outside));
            Assert.That(deadline.IsCommitDue(deadline.CommitAt), Is.True);
            Assert.That(deadline.IsCommitDue(deadline.CommitAt - 0.001), Is.False);
        }
    }

    [Test]
    public void BarrierWindow_NegativePreSongTrigger_ProducesSignedPhases()
    {
        var parameters = new EliteFillPolicyParameters();
        var barrier = EliteFillBarrierCalculator.Calculate(PreSongFillTime, parameters);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(barrier.At(PreSongFillTime), Is.EqualTo(EliteFillBarrierPhase.Latched));
            Assert.That(barrier.At(PreSongFillTime + parameters.BarrierLatchSeconds - 0.001),
                Is.EqualTo(EliteFillBarrierPhase.Latched));
            Assert.That(barrier.At(PreSongFillTime + parameters.BarrierLatchSeconds + 0.001),
                Is.EqualTo(EliteFillBarrierPhase.Recovering));
            Assert.That(barrier.At(PreSongFillTime + parameters.BarrierLatchSeconds
                + parameters.BarrierRecoverySeconds), Is.EqualTo(EliteFillBarrierPhase.Clear));
        }
    }

    [Test]
    public void RuntimeScheduler_BuiltFromNegativePreSongNotes_ConstructsAndResolvesInOrder()
    {
        var notes = CreatePreSongFillNotes();
        var policy = EliteFillPolicyV1.Default;
        var scheduler = new EliteFillRuntimeScheduler(notes, policy,
            note => (0.140, 0.140));

        var physical = scheduler.PhysicalNotes;
        Assert.That(physical, Has.Count.EqualTo(4));
        Assert.That(physical.Select(note => note.Time), Is.Ordered);

        // The first pre-song hit is a real input; the rest must expire as misses at a
        // signed clock past their grace deadlines, committing strictly in chart order.
        var kickHit = scheduler.FindCandidate((int) FourLaneDrumPad.Kick, PreSongFillTime);
        Assert.That(kickHit, Is.Not.Null);
        Assert.That(scheduler.TryAdjudicate(kickHit!, true, PreSongFillTime, out var kickCommits), Is.True);
        Assert.That(kickCommits.Select(commit => commit.Note), Is.EqualTo(new[] { kickHit }));

        var resolved = new List<EliteFillRuntimeScheduler.Commit>();
        resolved.AddRange(scheduler.ResolveCadence(-0.9));
        resolved.AddRange(scheduler.Expire(-0.9));
        Assert.That(scheduler.PendingCount, Is.Zero);
        Assert.That(scheduler.CommittedCount, Is.EqualTo(4));
        Assert.That(resolved.Where(commit => !commit.WasHit).Select(commit => commit.Note.Time),
            Is.EqualTo(new[] { -1.278, -1.178, -1.078 }));
    }

    [Test]
    public void YargDrumsEngine_WithNegativePreSongFill_LoadsUpdatesAndHitsWithoutValidationFailure()
    {
        // Reproduces the reported Foreplay/Longtime failure: a chart whose intro fill
        // sits before the zero anchor. Engine construction (which freezes every note's
        // fill deadline) and every per-frame scheduler call used to throw
        // ArgumentOutOfRangeException while the clock was still negative.
        var notes = CreatePreSongFillNotes();
        var diff = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert,
            notes, new List<Phrase>(), new List<TextEvent>());

        YargDrumsEngine engine = null!;
        Assert.DoesNotThrow(() => engine = CreateEngine(diff),
            "engine construction must not reject negative pre-song note times");

        var hitCallbacks = new List<DrumNote>();
        engine.OnNoteHit += (_, note) => hitCallbacks.Add(note);

        Assert.DoesNotThrow(() => engine.Update(PreSongFillTime - 0.2),
            "per-frame scheduler calls with a negative pre-song clock must not throw");

        Hit(engine, PreSongFillTime, DrumsAction.Kick);
        Hit(engine, PreSongFillTime, DrumsAction.RedDrum);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diff.Notes[0].WasHit, Is.True);
            Assert.That(diff.Notes[1].WasHit, Is.True);
            Assert.That(hitCallbacks.Select(note => note.Time),
                Does.Contain(PreSongFillTime));
        }

        // Advancing through the remaining fill (still pre-song) resolves the rest
        // without validation failures or spam. -0.9 is past the final tom's grace
        // deadline (-1.078 + 0.050 entry + 0.050 grace = -0.978).
        Assert.DoesNotThrow(() => engine.Update(-0.9));
        Assert.That(diff.Notes.Skip(2).All(note => note.WasMissed), Is.True,
            "unhit pre-song fill notes expire as misses on a negative clock");
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
    }

    private static List<DrumNote> CreatePreSongFillNotes()
    {
        // Foreplay/Longtime-style intro: kick plus a three-hit tom fill entirely
        // before the zero anchor. Ticks stay positive chart coordinates; only the
        // delay-shifted seconds are negative.
        var specs = new (double Time, FourLaneDrumPad Pad)[]
        {
            (PreSongFillTime, FourLaneDrumPad.Kick),
            (PreSongFillTime, FourLaneDrumPad.RedDrum),
            (-1.178, FourLaneDrumPad.RedDrum),
            (-1.078, FourLaneDrumPad.RedDrum),
        };

        var notes = new List<DrumNote>();
        for (var i = 0; i < specs.Length; i++)
        {
            var (time, pad) = specs[i];
            var source = new EliteDrumSourceDefinition($"prefill-{i}", i, (int) pad,
                (uint) (480 + 96 * i), 0, time);
            var origin = new EliteDrumConversionOrigin(source);
            notes.Add(new DrumNote(pad, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None,
                time, source.StartTick, false, DrumStem.Else, origin));
        }

        for (var i = 1; i < notes.Count; i++)
        {
            notes[i - 1].NextNote = notes[i];
            notes[i].PreviousNote = notes[i - 1];
        }

        return notes;
    }

    private static YargDrumsEngine CreateEngine(InstrumentDifficulty<DrumNote> diff)
    {
        var hitWindow = new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, 0.16, 0.25);
        var parameters = new DrumsEngineParameters(hitWindow, 4, StarThresholds, SoloThresholds,
            DrumsEngineParameters.DrumMode.ProFourLane, false, true,
            DrumsEngineParameters.ELITE_FILL_RULESET_V1, 1f);
        return new YargDrumsEngine(diff, CreateSyncTrack(), parameters, isBot: false, false);
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
}
