// pattern: Imperative Shell
using System;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Extensions;
using YARG.Core.Input;
using YARG.Core.IO;
using YARG.Core.Replays;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Replays;

public sealed class NativeEliteHatClosureReplayTests
{
    private static DrumsEngineParameters Parameters(bool enabled, double window = .03)
    {
        var p = NativeEliteReplayFixture.Parameters();
        return new(p.HitWindow, p.MaxMultiplier, p.StarMultiplierThresholds,
            p.SoloBonusStarMultiplierThresholds, p.Mode, p.NoStarPowerOverlap, p.EnableLanes,
            hiHatClosureSuppression: enabled, hiHatClosureWindow: window) { SongSpeed = 1 };
    }

    [Test]
    public void ParameterRoundTripsPreserveOptionWindowAndPayloadBoundary()
    {
        var random = new Random(731);
        for (int i = 0; i < 40; i++)
        {
            bool enabled = i % 2 == 0;
            double window = .001 + random.NextDouble() * .099;
            var p = Parameters(enabled, window);
            using var memory = new MemoryStream();
            using var writer = new BinaryWriter(memory);
            p.Serialize(writer);
            writer.Write(0x12345678);
            using var array = FixedArray<byte>.Alloc((int)memory.Length);
            memory.ToArray().CopyTo(array.Span);
            var stream = new FixedArrayStream(array);
            var read = new DrumsEngineParameters(ref stream, ReplayIO.REPLAY_VERSIONS.CURRENT);
            Assert.That(read.HiHatClosureSuppression, Is.EqualTo(enabled));
            Assert.That(read.HiHatClosureWindow, Is.EqualTo(window));
            Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
        }
    }

    [Test]
    public void HistoricalReplayPayloadHasDisabledClosureOptionAndCorrectBoundary()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        Parameters(true).Serialize(writer);
        memory.SetLength(memory.Length - 11); // v23/v25 add ruleset/pair-policy bytes after v22's bool and double.
        memory.Position = memory.Length;
        writer.Write(0x12345678);
        using var array = FixedArray<byte>.Alloc((int)memory.Length);
        memory.ToArray().CopyTo(array.Span);
        var stream = new FixedArrayStream(array);
        var read = new DrumsEngineParameters(ref stream, 21);
        Assert.That(read.HiHatClosureSuppression, Is.False);
        Assert.That(read.HiHatClosureWindow, Is.EqualTo(.03));
        Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }

    [TestCase(0)]
    [TestCase(-.01)]
    [TestCase(.101)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidWindowIsRejected(double window) =>
        Assert.That(() => Parameters(true, window), Throws.TypeOf<InvalidDataException>());

    [TestCase(1.02, 1)]
    [TestCase(1.05, 2)]
    public void ReplayPreservesLiveSnapshotHorizonIncludingPendingFinalInputs(double savedAt, int hits)
    {
        var chart = NativeEliteReplayFixture.Chart();
        var first = NativeEliteReplayFixture.EliteNote(EliteDrumPad.Snare, .5);
        var stomp = NativeEliteReplayFixture.EliteNote(EliteDrumPad.HatPedal, 1);
        var later = NativeEliteReplayFixture.EliteNote(EliteDrumPad.Ride, 2);
        first.NextNote = stomp; stomp.PreviousNote = first;
        stomp.NextNote = later; later.PreviousNote = stomp;
        chart.EliteDrums.AddDifficulty(Difficulty.Expert,
            new(Instrument.EliteDrums, Difficulty.Expert, new() { first, stomp, later }, new(), new()));
        var parameters = Parameters(true);
        var inputs = new[] { GameInput.Create(.5, EliteDrumsAction.EliteSnare, .8f),
            GameInput.Create(1, EliteDrumsAction.EliteClosedHiHat, .4f),
            GameInput.Create(1.02, EliteDrumsAction.EliteStomp, .6f) };
        var engine = new EliteDrumsEngine(chart.EliteDrums.GetDifficulty(Difficulty.Expert).Clone(),
            chart.SyncTrack, parameters, false, true);
        engine.SetSpeed(1);
        foreach (var recorded in inputs) { var input = recorded; engine.QueueInput(ref input); }
        // Pause saves use raw input time and the current delayed stats; they must
        // not prematurely resolve a pair whose companion could arrive on resume.
        engine.Update(savedAt);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(hits));
        var data = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(
            NativeEliteReplayFixture.Profile(Instrument.EliteDrums), parameters, engine.EngineStats, inputs));
        Assert.That(((DrumsEngineParameters)data.Frames[0].EngineParameters).HiHatClosureSuppression, Is.True);
        var info = new ReplayInfo("", "", ReplayIO.REPLAY_VERSIONS.CURRENT, 0, default,
            "", "", "", default, DateTime.UtcNow, 1, savedAt, 0, default,
            Array.Empty<PauseInfo>(), false, Array.Empty<ReplayStats>());
        var result = YARG.Core.Replays.Analyzer.ReplayAnalyzer.AnalyzeReplay(chart, info, data)[0];
        Assert.That(result.Passed, Is.True, result.StatLog);
    }
}
