// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;
using static YARG.Core.UnitTests.Engine.EliteHiHatSustainTests;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteHiHatSustainLifecycleTests
{
    private static void SubscribeToStarted(EliteDrumsEngine engine, Action<EliteHiHatSustain, double> observer) =>
        engine.OnHiHatSustainStarted += observer;

    [TestCase(false)]
    [TestCase(true)]
    public void StartPublishesTheActiveIntervalAndInputTimeBeforeNoteHit(bool bot)
    {
        var engine = Create(new[] { Note(1, 1920) }, bot: bot);
        var events = new List<string>();
        double expected = bot ? 1 : 1.03;
        SubscribeToStarted(engine, (sustain, time) =>
        {
            Assert.That(engine.ActiveHiHatSustain, Is.SameAs(sustain));
            Assert.That(sustain, Is.SameAs(engine.HiHatSustains.Single()));
            Assert.That(time, Is.EqualTo(expected));
            Assert.That(time, Is.EqualTo(engine.CurrentTime));
            events.Add("start");
        });
        engine.OnNoteHit += (_, _) => events.Add("hit");

        if (bot) engine.Update(1.03);
        else Press(engine, 1.03, EliteDrumsAction.EliteOpenHiHat);

        Assert.That(events, Is.EqualTo(new[] { "start", "hit" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SimultaneousGeneratorsPublishOneStartInEitherOrder(bool splashFirst)
    {
        var open = Note(1, 1920);
        open.AddChildNote(Note(1, 1920, EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash));
        var engine = Create(new[] { open });
        var starts = new List<EliteHiHatSustain>();
        SubscribeToStarted(engine, (sustain, _) => starts.Add(sustain));

        foreach (var action in splashFirst
            ? new[] { EliteDrumsAction.EliteSplash, EliteDrumsAction.EliteOpenHiHat }
            : new[] { EliteDrumsAction.EliteOpenHiHat, EliteDrumsAction.EliteSplash })
            Press(engine, 1, action);

        Assert.That(starts, Is.EqualTo(new[] { engine.HiHatSustains.Single() }));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
    }

    [Test]
    public void EarlyRenewalPublishesEndThenStartThenTheNewNoteHit()
    {
        var engine = Create(new[] { Note(1), Note(1.5, 1920) });
        var events = new List<string>();
        SubscribeToStarted(engine, (sustain, _) =>
        {
            Assert.That(engine.ActiveHiHatSustain, Is.SameAs(sustain));
            events.Add($"start:{sustain.StartTick}");
        });
        engine.OnHiHatSustainEnded += (sustain, broken) =>
        {
            Assert.That(engine.ActiveHiHatSustain, Is.Null);
            Assert.That(broken, Is.False);
            events.Add($"end:{sustain.StartTick}");
        };
        engine.OnNoteHit += (_, note) => events.Add($"hit:{note.Tick}");

        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, 1.45, EliteDrumsAction.EliteOpenHiHat);

        Assert.That(events, Is.EqualTo(new[] {
            "start:960", "hit:960", "end:960", "start:1440", "hit:1440" }));
    }

    [Test]
    public void SkippedNoteMissPrecedesTheNewGeneratorStartNotification()
    {
        var snare = Note(1.04, pad: EliteDrumPad.Snare);
        var renewal = Note(1.125);
        var engine = Create(new[] { Note(1), snare, renewal, Note(1.25, 1920) });
        var events = new List<string>();
        int starts = 0;
        SubscribeToStarted(engine, (sustain, time) =>
        {
            Assert.That(engine.ActiveHiHatSustain, Is.SameAs(sustain));
            starts++;
            events.Add("start");
            if (ReferenceEquals(sustain, engine.HiHatSustains[1]))
                Assert.That(time, Is.EqualTo(1.1));
        });
        engine.OnNoteMissed += (_, note) =>
        {
            Assert.That(note, Is.SameAs(snare));
            Assert.That(engine.ActiveHiHatSustain, Is.SameAs(engine.HiHatSustains[1]),
                "Active assignment stays before skip adjudication; only its notification is deferred.");
            events.Add("miss");
        };
        engine.OnNoteHit += (_, note) =>
        {
            if (ReferenceEquals(note, renewal)) events.Add("hit");
        };

        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        events.Clear();
        Press(engine, 1.1, EliteDrumsAction.EliteOpenHiHat);

        Assert.That(events, Is.EqualTo(new[] { "miss", "start", "hit" }));
        Assert.That(starts, Is.EqualTo(2));
        Assert.That(snare.WasMissed, Is.True);
        Assert.That(renewal.WasHit, Is.True);
    }

    [Test]
    public void ReleasedSharedIntervalPublishesAFreshStartWhenItsOtherGeneratorHits()
    {
        var open = Note(1, 1920);
        open.AddChildNote(Note(1, 1920, EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash));
        var engine = Create(new[] { open, Note(5, pad: EliteDrumPad.Snare) });
        var starts = new List<(EliteHiHatSustain Sustain, double Time)>();
        SubscribeToStarted(engine, (sustain, time) => starts.Add((sustain, time)));

        Press(engine, .92, EliteDrumsAction.EliteSplash);
        Press(engine, .94, EliteDrumsAction.EliteStomp);
        Assert.That(engine.ActiveHiHatSustain, Is.Null);
        Press(engine, 1.02, EliteDrumsAction.EliteOpenHiHat);

        var sustain = engine.HiHatSustains.Single();
        Assert.That(starts, Is.EqualTo(new[] { (sustain, .92), (sustain, 1.02) }));
    }

    [Test]
    public void OptionalActivationStartsItsSustainWithoutANoteHitCallback()
    {
        var open = Note(1, 1920);
        open.ActivateFlag(DrumNoteFlags.StarPowerActivator);
        var engine = Create(new[] { open });
        engine.EngineStats.StarPowerTickAmount = engine.TicksPerHalfSpBar;
        var starts = new List<(EliteHiHatSustain Sustain, double Time)>();
        int noteHits = 0;
        SubscribeToStarted(engine, (sustain, time) =>
        {
            Assert.That(engine.ActiveHiHatSustain, Is.SameAs(sustain));
            Assert.That(time, Is.EqualTo(engine.CurrentTime));
            starts.Add((sustain, time));
        });
        engine.OnNoteHit += (_, _) => noteHits++;

        engine.Update(1.11);

        Assert.That(open.WasHit, Is.True);
        Assert.That(noteHits, Is.Zero);
        Assert.That(starts, Has.Count.EqualTo(1));
        Assert.That(starts[0].Sustain, Is.SameAs(engine.HiHatSustains.Single()));
        Assert.That(starts[0].Time, Is.InRange(1.1, 1.11));
    }

    [Test]
    public void ReplayReconstructionRebuildsStartsAfterEveryReset()
    {
        var engine = Create(new[] { Note(1), Note(1.5, 1920) });
        var starts = new List<(EliteHiHatSustain Sustain, double Time)>();
        SubscribeToStarted(engine, (sustain, time) => starts.Add((sustain, time)));
        var inputs = new[] { GameInput.Create(1.02, EliteDrumsAction.EliteOpenHiHat, .8f),
            GameInput.Create(1.45, EliteDrumsAction.EliteOpenHiHat, .8f) };

        Assert.That(engine.ProcessUpToTime(1.47, inputs), Is.EqualTo(2));
        Assert.That(starts, Is.EqualTo(new[] {
            (engine.HiHatSustains[0], 1.02), (engine.HiHatSustains[1], 1.45) }));
        starts.Clear();
        Assert.That(engine.ProcessUpToTime(.5, inputs), Is.Zero);
        Assert.That(starts, Is.Empty);
        Assert.That(engine.ActiveHiHatSustain, Is.Null);
        Assert.That(engine.ProcessUpToTime(1.47, inputs), Is.EqualTo(2));
        Assert.That(starts, Is.EqualTo(new[] {
            (engine.HiHatSustains[0], 1.02), (engine.HiHatSustains[1], 1.45) }));
    }
}
