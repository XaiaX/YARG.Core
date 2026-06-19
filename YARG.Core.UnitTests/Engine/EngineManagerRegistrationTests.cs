using System;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Guitar;
using YARG.Core.Engine.Guitar.Engines;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Engine;

/// <summary>
/// Tests for EngineManager registration overloads, including free-vocals support.
/// Follows the same pattern as EngineManagerTester (extends EngineTester for chart loading).
/// </summary>
[TestFixture]
public sealed class EngineManagerRegistrationTests : EngineTester
{
    private static readonly GuitarEngineParameters GuitarParams =
        EnginePreset.Default.FiveFretGuitar.Create(StarMultiplierThresholds, SoloBonusStarMultiplierThresholds, false);

    private static readonly VocalsEngineParameters VocalsParams = new(
        new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0, 0),
        4,
        new float[] { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f },
        new float[] { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f },
        1.5f, 0.5f, 0.75, 60.0, true, 1000);

    // ================================================================
    // Free overload produces EngineContainer with HarmonyIndex == FREE_HARMONY_INDEX
    // ================================================================

    [Test]
    public void FreeOverload_ProducesContainerWithFreeHarmonyIndex()
    {
        var manager = new EngineManager();
        var chart = GetChart();
        var guitarEngine = CreateGuitarEngine(chart);

        var container = manager.Register(guitarEngine, Instrument.Vocals, freeVocals: true, chart, RockMeterPreset.Normal);

        Assert.That(container.HarmonyIndex, Is.EqualTo(EngineManager.FREE_HARMONY_INDEX));
        Assert.That(container.Instrument, Is.EqualTo(Instrument.Vocals));
    }

    // ================================================================
    // Free and indexed engines can coexist on Instrument.Vocals
    // ================================================================

    [Test]
    public void FreeAndIndexedEngines_CoexistOnVocals()
    {
        var manager = new EngineManager();
        var chart = GetChart();

        var freeEngine = CreateGuitarEngine(chart);
        var harm0Engine = CreateGuitarEngine(chart);
        var harm1Engine = CreateGuitarEngine(chart);

        // Register free vocals engine
        var freeContainer = manager.Register(freeEngine, Instrument.Vocals, freeVocals: true, chart, RockMeterPreset.Normal);

        // Register harmony-indexed vocals engines (harmonyIndex 0 and 1)
        var harm0Container = manager.Register(harm0Engine, Instrument.Vocals, harmonyIndex: 0, chart, RockMeterPreset.Normal);
        var harm1Container = manager.Register(harm1Engine, Instrument.Vocals, harmonyIndex: 1, chart, RockMeterPreset.Normal);

        Assert.That(manager.Engines, Has.Count.EqualTo(3));
        Assert.That(freeContainer.HarmonyIndex, Is.EqualTo(EngineManager.FREE_HARMONY_INDEX));
        Assert.That(harm0Container.HarmonyIndex, Is.EqualTo(0));
        Assert.That(harm1Container.HarmonyIndex, Is.EqualTo(1));

        // All containers are distinct
        Assert.That(freeContainer.EngineId, Is.Not.EqualTo(harm0Container.EngineId));
        Assert.That(freeContainer.EngineId, Is.Not.EqualTo(harm1Container.EngineId));
        Assert.That(harm0Container.EngineId, Is.Not.EqualTo(harm1Container.EngineId));
    }

    // ================================================================
    // Indexed overload still works for harmonyIndex = 0, 1, 2, 3
    // ================================================================

    [Test]
    public void IndexedOverload_WorksForHarmonyIndexZeroThroughThree()
    {
        var manager = new EngineManager();
        var chart = GetChart();

        for (int i = 0; i <= 3; i++)
        {
            var engine = CreateGuitarEngine(chart);
            var container = manager.Register(engine, Instrument.Vocals, harmonyIndex: i, chart, RockMeterPreset.Normal);
            Assert.That(container.HarmonyIndex, Is.EqualTo(i), $"HarmonyIndex should be {i}");
        }

        Assert.That(manager.Engines, Has.Count.EqualTo(4));
    }

    // ================================================================
    // Free overload rejects freeVocals == false with ArgumentException
    // ================================================================

    [Test]
    public void FreeOverload_RejectsFalseFreeVocals()
    {
        var manager = new EngineManager();
        var chart = GetChart();
        var engine = CreateGuitarEngine(chart);

        Assert.Throws<ArgumentException>(() =>
        {
            manager.Register(engine, Instrument.Vocals, freeVocals: false, chart, RockMeterPreset.Normal);
        });
    }

    // ================================================================
    // Indexed overload logs failure on negative harmonyIndex but does not throw.
    // YargLogger.FailFormat only breaks in a debugger; in unit tests it logs
    // and continues. Verify the container is still created with the invalid
    // index (the guard is a developer warning, not a hard gate).
    // ================================================================

    [Test]
    public void IndexedOverload_NegativeHarmonyIndex_StillCreatesContainer()
    {
        // NOTE: YargLogger.FailFormat does not throw -- it only breaks when a
        // debugger is attached. This test confirms the registration completes
        // and the HarmonyIndex is stored as-passed, so the runtime guard is
        // visible to developers debugging but does not crash production.
        var manager = new EngineManager();
        var chart = GetChart();
        var engine = CreateGuitarEngine(chart);

        // This will log a failure via YargLogger but not throw
        var container = manager.Register(engine, Instrument.Vocals, harmonyIndex: -1, chart, RockMeterPreset.Normal);

        // The container is still created -- the guard is advisory, not blocking
        Assert.That(container, Is.Not.Null);
        Assert.That(container.HarmonyIndex, Is.EqualTo(-1));
    }

    // ================================================================
    // Default overload (no index) uses harmonyIndex 0
    // ================================================================

    [Test]
    public void DefaultOverload_UsesHarmonyIndexZero()
    {
        var manager = new EngineManager();
        var chart = GetChart();
        var engine = CreateGuitarEngine(chart);

        var container = manager.Register(engine, Instrument.Vocals, chart, RockMeterPreset.Normal);

        Assert.That(container.HarmonyIndex, Is.EqualTo(0));
    }

    // ================================================================
    // FREE_HARMONY_INDEX constant is -1
    // ================================================================

    [Test]
    public void FreeHarmonyIndex_IsMinusOne()
    {
        Assert.That(EngineManager.FREE_HARMONY_INDEX, Is.EqualTo(-1));
    }

    // ================================================================
    // Helper: create a guitar engine from the test chart
    // ================================================================

    private YargFiveFretGuitarEngine CreateGuitarEngine(SongChart chart)
    {
        var notes = chart.FiveFretGuitar.GetDifficulty(Difficulty.Expert);
        return new YargFiveFretGuitarEngine(notes, chart.SyncTrack, GuitarParams, isBot: false);
    }
}
