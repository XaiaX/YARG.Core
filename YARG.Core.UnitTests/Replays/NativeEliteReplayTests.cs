using System;
using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.IO;
using YARG.Core.Replays;
using YARG.Core.Replays.Analyzer;

namespace YARG.Core.UnitTests.Replays;

internal static class NativeEliteReplayFixture
{
    internal static readonly float[] Thresholds = { 1f, 2f, 3f, 4f, 5f, 6f };

    internal static DrumsEngineParameters Parameters() =>
        EnginePreset.Default.Drums.Create(Thresholds, Thresholds, DrumsEngineParameters.DrumMode.ProFourLane);

    internal static YargProfile Profile(Instrument instrument) => new(Guid.NewGuid())
    {
        GameMode = GameMode.EliteDrums,
        CurrentInstrument = instrument,
        CurrentDifficulty = Difficulty.Expert,
    };

    internal static EliteDrumNote EliteNote(EliteDrumNote.EliteDrumPad pad, double time) =>
        new(pad, DrumNoteType.Neutral, EliteDrumNote.EliteDrumsHatState.Indifferent,
            EliteDrumNote.EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumNote.EliteDrumsChannelFlag.None, time, (uint) (time * 960), false);

    internal static SongChart EliteChart()
    {
        var chart = Chart();
        chart.EliteDrums.AddDifficulty(Difficulty.Expert,
            new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
                new List<EliteDrumNote>
                {
                    EliteNote(EliteDrumNote.EliteDrumPad.Snare, 1),
                    EliteNote(EliteDrumNote.EliteDrumPad.Ride, 2),
                    EliteNote(EliteDrumNote.EliteDrumPad.Tom1, 3),
                }, new(), new()));
        return chart;
    }

    internal static SongChart Chart()
    {
        var chart = new SongChart(480);
        chart.SyncTrack.Tempos.Add(new TempoChange(120, 0, 0));
        return chart;
    }

    internal static GameInput[] EliteInputs() => new[]
    {
        GameInput.Create(1, EliteDrumsAction.EliteSnare, 0.8f),
        GameInput.Create(2, EliteDrumsAction.EliteRide, 0.8f),
    };

    internal static DrumsStats PlayLive(EliteDrumsEngine engine, GameInput[] inputs)
    {
        engine.Update(-2);
        foreach (var recorded in inputs)
        {
            var input = recorded;
            engine.QueueInput(ref input);
            engine.Update(input.Time + 0.02);
        }
        engine.Update(3.5);
        return new DrumsStats(engine.EngineStats);
    }

    internal static ReplayData Data(YargProfile profile, DrumsEngineParameters parameters, DrumsStats stats,
        GameInput[] inputs) => new(new Dictionary<Guid, ColorProfile>(), new Dictionary<Guid, CameraPreset>(),
        new Dictionary<Guid, RockMeterPreset>(), false,
        new[] { new ReplayFrame(profile, parameters, stats, inputs) }, Array.Empty<double>());

    internal static ReplayData RoundTrip(ReplayData data)
    {
        var bytes = data.Serialize().ToArray();
        var buffer = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(buffer.Span);
        return new ReplayData(new FixedArrayStream(buffer), ReplayIO.REPLAY_VERSIONS.CURRENT,
            new ReplayReadOptions());
    }

    internal static AnalysisResult Analyze(SongChart chart, ReplayData data)
    {
        var info = new ReplayInfo("", "", ReplayIO.REPLAY_VERSIONS.CURRENT, 0, default,
            "", "", "", default, DateTime.UtcNow, 1, 3.5, 0, default,
            Array.Empty<PauseInfo>(), false, Array.Empty<ReplayStats>());
        var results = ReplayAnalyzer.AnalyzeReplay(chart, info, data);
        Assert.That(results, Has.Length.EqualTo(1));
        return results[0];
    }

    internal static void AssertLiveMatchesReplay(DrumsStats live, AnalysisResult result)
    {
        var replayed = (DrumsStats) result.ResultStats;
        Assert.Multiple(() =>
        {
            Assert.That(result.Passed, Is.True, result.StatLog);
            Assert.That(result.OriginalStats.CommittedScore, Is.EqualTo(live.CommittedScore));
            Assert.That(replayed.CommittedScore, Is.EqualTo(live.CommittedScore));
            Assert.That(replayed.NoteScore, Is.EqualTo(live.NoteScore));
            Assert.That(replayed.NotesHit, Is.EqualTo(live.NotesHit));
            Assert.That(replayed.NotesMissed, Is.EqualTo(live.NotesMissed));
            Assert.That(replayed.TotalNotes, Is.EqualTo(live.TotalNotes));
            Assert.That(replayed.Combo, Is.EqualTo(live.Combo));
            Assert.That(replayed.Overhits, Is.EqualTo(live.Overhits));
        });
    }
}

[TestFixture]
public sealed class NativeEliteReplayRoundTripTests
{
    [Test]
    public void NativeProfileInputsParametersAndScoredStatsSurviveSerialization()
    {
        var chart = NativeEliteReplayFixture.EliteChart();
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        var parameters = NativeEliteReplayFixture.Parameters();
        var inputs = NativeEliteReplayFixture.EliteInputs();
        var engine = new EliteDrumsEngine(chart.EliteDrums.GetDifficulty(Difficulty.Expert).Clone(),
            chart.SyncTrack, parameters, false, true);
        var live = NativeEliteReplayFixture.PlayLive(engine, inputs);
        var frame = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters, live, inputs)).Frames[0];

        Assert.Multiple(() =>
        {
            Assert.That(live.CommittedScore, Is.GreaterThan(0));
            Assert.That(live.NotesHit, Is.EqualTo(2));
            Assert.That(live.NotesMissed, Is.EqualTo(1));
            Assert.That(frame.Profile.GameMode, Is.EqualTo(GameMode.EliteDrums));
            Assert.That(frame.Profile.CurrentInstrument, Is.EqualTo(Instrument.EliteDrums));
            Assert.That(frame.Profile.CurrentDifficulty, Is.EqualTo(Difficulty.Expert));
            Assert.That(frame.Profile.EliteDrumsDownchartTarget, Is.Null);
            Assert.That(((DrumsEngineParameters) frame.EngineParameters).Mode, Is.EqualTo(parameters.Mode));
            Assert.That(frame.Inputs, Has.Length.EqualTo(inputs.Length));
            Assert.That(frame.Inputs[0].Action, Is.EqualTo(inputs[0].Action));
            Assert.That(frame.Inputs[0].Time, Is.EqualTo(inputs[0].Time));
            Assert.That(frame.Inputs[0].Integer, Is.EqualTo(inputs[0].Integer));
            Assert.That(frame.Inputs[1].Action, Is.EqualTo(inputs[1].Action));
            Assert.That(frame.Stats.CommittedScore, Is.EqualTo(live.CommittedScore));
            Assert.That(frame.Stats.NotesHit, Is.EqualTo(live.NotesHit));
            Assert.That(frame.Stats.NotesMissed, Is.EqualTo(live.NotesMissed));
        });
    }
}

[TestFixture]
public sealed class NativeEliteReplayAnalysisTests
{
    [Test]
    public void SerializedNativeEliteInputsReplayWithLiveScoreHitsAndMiss()
    {
        var chart = NativeEliteReplayFixture.EliteChart();
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        var parameters = NativeEliteReplayFixture.Parameters();
        var inputs = NativeEliteReplayFixture.EliteInputs();
        var live = NativeEliteReplayFixture.PlayLive(new EliteDrumsEngine(
            chart.EliteDrums.GetDifficulty(Difficulty.Expert).Clone(), chart.SyncTrack, parameters, false, true), inputs);
        var data = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters, live, inputs));

        Assert.That(data.GetEliteDrumsDownchartOutputs(), Is.Null);
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(chart, data));
    }
}

[TestFixture]
public sealed class NativeEliteFallbackReplayIdentityTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void EliteModeFallbackToNativeProTrackRetainsIdentityAndLiveScoring(bool nativeEliteAvailable)
    {
        var chart = nativeEliteAvailable ? NativeEliteReplayFixture.EliteChart() : NativeEliteReplayFixture.Chart();
        var native = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert,
            new List<DrumNote>
            {
                new(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 1, 960),
                new(FourLaneDrumPad.BlueDrum, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 2, 1920),
            }, new(), new());
        chart.ProDrums.AddDifficulty(Difficulty.Expert, native);
        var profile = NativeEliteReplayFixture.Profile(Instrument.ProDrums);
        if (!nativeEliteAvailable)
        {
            Assert.That(DrumDifficultySelector.ResolveNativeEliteRequest(chart, profile, Difficulty.Expert),
                Is.EqualTo(Instrument.ProDrums));
        }
        var parameters = NativeEliteReplayFixture.Parameters();
        var inputs = new[]
        {
            GameInput.Create(1, EliteDrumsAction.FourLaneRedDrum, 0.8f),
            GameInput.Create(2, EliteDrumsAction.FourLaneBlueDrum, 0.8f),
        };
        var engine = new YargDrumsEngine(native.Clone(), chart.SyncTrack, parameters, false, true);
        engine.Update(-2);
        foreach (var recorded in inputs)
        {
            var input = recorded;
            engine.QueueInput(ref input);
            engine.Update(input.Time + 0.02);
        }
        engine.Update(3.5);
        var live = new DrumsStats(engine.EngineStats);
        var data = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters, live, inputs));
        var restored = data.Frames[0].Profile;

        Assert.Multiple(() =>
        {
            Assert.That(live.NotesHit, Is.EqualTo(2));
            Assert.That(live.CommittedScore, Is.GreaterThan(0));
            Assert.That(restored.GameMode, Is.EqualTo(GameMode.EliteDrums));
            Assert.That(restored.CurrentInstrument, Is.EqualTo(Instrument.ProDrums));
            Assert.That(restored.EliteDrumsDownchartTarget, Is.Null);
            Assert.That(DrumDifficultySelector.UsesNativeEliteTrack(restored), Is.False);
            Assert.That(DrumDifficultySelector.SelectTrack(chart, restored), Is.SameAs(chart.ProDrums));
            Assert.That(data.GetEliteDrumsDownchartOutputs(), Is.Null);
        });
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, NativeEliteReplayFixture.Analyze(chart, data));
    }
}
