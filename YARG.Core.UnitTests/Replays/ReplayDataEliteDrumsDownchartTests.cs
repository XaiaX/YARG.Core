using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.IO;
using YARG.Core.Replays;

namespace YARG.Core.UnitTests.Replays;

/// <summary>
/// Covers the replay-side handling of the explicit "Elite (To …)" downchart targets.
/// A replay recorded while one was active must carry the target through serialization and
/// expose it so every chart-loading path can rebuild the downchart variant that was played.
/// Replays recorded before the feature existed must keep reading as "no target".
/// </summary>
[TestFixture]
public sealed class ReplayDataEliteDrumsDownchartTests
{
    private const int REPLAY_VERSION = 17;

    private static readonly float[] StarMultiplierThresholds =
        { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f };
    private static readonly float[] SoloBonusStarMultiplierThresholds =
        { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f };

    private static DrumsEngineParameters CreateDrumsParameters(
        DrumsEngineParameters.DrumMode mode = DrumsEngineParameters.DrumMode.ProFourLane)
    {
        return new DrumsEngineParameters(
            new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1.0, 1.0, 0.15, 0.25),
            4,
            StarMultiplierThresholds,
            SoloBonusStarMultiplierThresholds,
            mode,
            false,
            true);
    }

    private static ReplayFrame CreateDrumsFrame(Instrument instrument,
        Instrument? downchartTarget, GameMode gameMode)
    {
        var profile = new YargProfile(Guid.NewGuid())
        {
            CurrentInstrument = instrument,
            CurrentDifficulty = Difficulty.Expert,
            GameMode = gameMode,
            EliteDrumsDownchartTarget = downchartTarget,
        };

        return new ReplayFrame(profile, CreateDrumsParameters(), new DrumsStats(),
            Array.Empty<GameInput>());
    }

    private static ReplayData RoundTrip(ReplayData original)
    {
        var bytes = original.Serialize().ToArray();

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        return new ReplayData(stream, REPLAY_VERSION, new ReplayReadOptions());
    }

    private static ReplayData CreateData(params ReplayFrame[] frames)
    {
        return new ReplayData(
            new Dictionary<Guid, ColorProfile>(),
            new Dictionary<Guid, CameraPreset>(),
            new Dictionary<Guid, RockMeterPreset>(),
            noFail: false,
            frames,
            Array.Empty<double>());
    }

    // A replay recorded with an explicit target must surface it so the chart can be
    // loaded with the downchart variant that was actually played.
    [Test]
    public void ExplicitTarget_SurvivesRoundTrip_AndIsExposedAsDownchartOutput()
    {
        foreach (var target in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums })
        {
            var original = CreateData(CreateDrumsFrame(target, target, GameMode.EliteDrums));
            var roundTripped = RoundTrip(original);

            Assert.That(roundTripped.Frames[0].Profile.EliteDrumsDownchartTarget, Is.EqualTo(target),
                $"Target {target} should survive replay serialization");
            Assert.That(roundTripped.Frames[0].Profile.CurrentInstrument, Is.EqualTo(target),
                "CurrentInstrument must stay equal to the target so replay-side engine/highway selection matches");

            var outputs = roundTripped.GetEliteDrumsDownchartOutputs();
            Assert.That(outputs, Is.Not.Null, $"Target {target} should be reported as a downchart output");
            Assert.That(outputs, Is.EquivalentTo(new[] { target }));
        }
    }

    // Replays where nobody used the feature must report null, which keeps chart loading
    // byte-for-byte identical to a normal load (the "no outputs" contract of LoadChart).
    [Test]
    public void NoTarget_Anywhere_ReportsNullDownchartOutputs()
    {
        var original = CreateData(
            CreateDrumsFrame(Instrument.ProDrums, null, GameMode.FourLaneDrums),
            CreateDrumsFrame(Instrument.FiveLaneDrums, null, GameMode.FiveLaneDrums));
        var roundTripped = RoundTrip(original);

        Assert.That(roundTripped.Frames[0].Profile.EliteDrumsDownchartTarget, Is.Null);
        Assert.That(roundTripped.Frames[1].Profile.EliteDrumsDownchartTarget, Is.Null);
        Assert.That(roundTripped.GetEliteDrumsDownchartOutputs(), Is.Null);
    }

    // A target byte outside the valid domain (malformed or stale data) must be rejected
    // at deserialization and never surface as a downchart output — it can never name a
    // chart that could actually be built.
    [Test]
    public void MalformedTargetByte_ReadsAsNoTarget_AndIsNotCollected()
    {
        var garbage = (Instrument) 99;
        var original = CreateData(CreateDrumsFrame(garbage, garbage, GameMode.EliteDrums));

        // The collector must skip the malformed value even on in-memory data.
        Assert.That(original.GetEliteDrumsDownchartOutputs(), Is.Null,
            "a target outside the valid domain must never be collected as an output");

        var roundTripped = RoundTrip(original);

        Assert.That(roundTripped.Frames[0].Profile.EliteDrumsDownchartTarget, Is.Null,
            "a malformed target byte is genuinely invalid and must deserialize as no target");
        Assert.That(roundTripped.GetEliteDrumsDownchartOutputs(), Is.Null);
    }

    // Non-drum profiles never carry a target, so they must not contribute outputs even
    // when sitting alongside a player who did use one.
    [Test]
    public void MixedPlayers_OnlyDrumTargetsAreCollected()
    {
        var original = CreateData(
            CreateDrumsFrame(Instrument.FourLaneDrums, Instrument.FourLaneDrums, GameMode.EliteDrums),
            CreateDrumsFrame(Instrument.ProDrums, Instrument.ProDrums, GameMode.FourLaneDrums),
            CreateDrumsFrame(Instrument.ProDrums, null, GameMode.FourLaneDrums));
        var roundTripped = RoundTrip(original);

        var outputs = roundTripped.GetEliteDrumsDownchartOutputs();
        Assert.That(outputs, Is.Not.Null);
        Assert.That(outputs, Is.EquivalentTo(new[]
        {
            Instrument.FourLaneDrums, Instrument.ProDrums
        }));
    }

    // Old replays serialize v12 profiles, which end before the downchart-target block.
    // Simulated here by hand-building a full frame the way an old replay would have written
    // it, then reading it back through the real ReplayFrame deserialization path.
    [Test]
    public void OldReplayProfileVersion12_ReadsAsNoTarget()
    {
        var bytes = BuildV12FrameBytes(name: "OldReplayProfile", instrument: Instrument.ProDrums);

        var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(fixedArray.Span);
        var stream = new FixedArrayStream(fixedArray);
        var frame = new ReplayFrame(ref stream, REPLAY_VERSION);

        Assert.Multiple(() =>
        {
            Assert.That(frame.Profile.Version, Is.EqualTo(12));
            Assert.That(frame.Profile.GameMode, Is.EqualTo(GameMode.EliteDrums));
            Assert.That(frame.Profile.CurrentInstrument, Is.EqualTo(Instrument.ProDrums));
            Assert.That(frame.Profile.EliteDrumsDownchartTarget, Is.Null,
                "Old replays predate the feature and must read as 'no target'");
        });
    }

    /// <summary>
    /// Hand-builds a v12 (pre-target) drums profile followed by everything else a
    /// ReplayFrame needs, so the frame can be deserialized the way an old replay would be.
    /// </summary>
    private static byte[] BuildV12FrameBytes(string name, Instrument instrument)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        new FourCC('R', 'P', 'F', 'M').Serialize(writer);
        WriteV12Profile(writer, name, instrument);

        // Engine parameters + stats + empty inputs, so the frame stream is complete.
        CreateDrumsParameters().Serialize(writer);
        new DrumsStats().Serialize(writer);
        writer.Write(0);

        return ms.ToArray();
    }

    private static void WriteV12Profile(BinaryWriter writer, string name, Instrument instrument)
    {
        writer.Write(12);
        writer.Write(name);

        for (int i = 0; i < 5; i++)
        {
            writer.Write(Guid.Empty);
        }

        writer.Write((byte) instrument);
        writer.Write((byte) Difficulty.Expert);
        writer.Write((ulong) Modifier.None);
        writer.Write((byte) 0);

        writer.Write(6.0f);
        writer.Write(1.0f);
        writer.Write(false);
        writer.Write(true);

        writer.Write(true);
        writer.Write((byte) 0);
        writer.Write((byte) 0);
        writer.Write((byte) 0);

        writer.Write((byte) StarPowerActivationType.RightmostNote);
        writer.Write((byte) GameMode.EliteDrums);
        writer.Write((byte) OpenLaneDisplayType.Never);

        var fourLane = new DrumsHighwayItem[]
        {
            DrumsHighwayItem.Red, DrumsHighwayItem.Yellow,
            DrumsHighwayItem.Blue, DrumsHighwayItem.Green
        };
        writer.Write((byte) fourLane.Length);
        foreach (var item in fourLane) writer.Write((byte) item);

        writer.Write((byte) fourLane.Length);
        foreach (var item in fourLane) writer.Write((byte) item);

        var fiveLane = new DrumsHighwayItem[]
        {
            DrumsHighwayItem.Red, DrumsHighwayItem.Yellow,
            DrumsHighwayItem.Blue, DrumsHighwayItem.Orange,
            DrumsHighwayItem.Green
        };
        writer.Write((byte) fiveLane.Length);
        foreach (var item in fiveLane) writer.Write((byte) item);

        // v12 trailer under the unified layout: RockMeterPreset (v9+) only.
        // No chart preference or downchart target — those are v14+ fields.
        writer.Write(Guid.Empty);
    }
}
