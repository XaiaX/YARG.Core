// pattern: Imperative Shell
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.IO;
using YARG.Core.Replays;
using YARG.Core.UnitTests.Engine;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Replays;

public sealed class EliteHiHatSustainReplayTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PedalFaultAndFinalSustainFullComboLossRoundTripAndAnalyze(bool assisted)
    {
        var chart = NativeEliteReplayFixture.Chart();
        var track = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new() { EliteHiHatSustainTests.Note(1, 1920) }, new(), new());
        chart.EliteDrums.AddDifficulty(Difficulty.Expert, track);
        var parameters = EliteHiHatSustainScoreTests.Parameters(legacy: true);
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.AutoHiHatPedal = assisted;
        var engine = new EliteDrumsEngine(track, chart.SyncTrack, parameters, false, true, assisted);
        var inputs = new[] { GameInput.Create(1, EliteDrumsAction.EliteOpenHiHat, .8f),
            GameInput.Create(2, EliteDrumsAction.EliteStomp, .8f) };
        var live = NativeEliteReplayFixture.PlayLive(engine, inputs);
        var data = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters, live, inputs));
        var result = NativeEliteReplayFixture.Analyze(chart, data);
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, result);
        Assert.That(((DrumsStats)result.OriginalStats).HiHatSustainBreaks, Is.EqualTo(1));
        Assert.That(((DrumsStats)result.ResultStats).HiHatSustainBreaks, Is.EqualTo(1));
        Assert.That(result.ResultStats.IsFullCombo, Is.False);
        Assert.That(result.ResultStats.Percent, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SplitGemScoreAndHarmlessReleaseAnalyzeIdentically(bool close)
    {
        var chart = NativeEliteReplayFixture.Chart();
        var track = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            new() { EliteHiHatSustainTests.Note(1, 1920) }, new(), new());
        chart.EliteDrums.AddDifficulty(Difficulty.Expert, track);
        var parameters = EliteHiHatSustainScoreTests.Parameters();
        var profile = NativeEliteReplayFixture.Profile(Instrument.EliteDrums);
        profile.OneFretHiHatSustains = true;
        var engine = new EliteDrumsEngine(track, chart.SyncTrack, parameters, false, true);
        var inputs = close ? new[] { GameInput.Create(1, EliteDrumsAction.EliteOpenHiHat, .8f),
            GameInput.Create(2, EliteDrumsAction.EliteStomp, .8f) } :
            new[] { GameInput.Create(1, EliteDrumsAction.EliteOpenHiHat, .8f) };
        var live = NativeEliteReplayFixture.PlayLive(engine, inputs);
        var data = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(profile, parameters, live, inputs));
        var result = NativeEliteReplayFixture.Analyze(chart, data);
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, result);
        Assert.That(live.TotalScore, Is.EqualTo(close ? 37 : 50));
        Assert.That(live.NoteScore, Is.EqualTo(25));
        Assert.That(live.Overhits, Is.Zero);
        Assert.That(live.IsFullCombo, Is.True);
        Assert.That(((DrumsStats)result.ResultStats).SustainScore, Is.EqualTo(live.SustainScore));
        Assert.That(data.Frames[0].Profile.OneFretHiHatSustains, Is.True);
    }

    [Test]
    public void Version25ParametersDisableSustainsWithoutConsumingTheNextPayload()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        NativeEliteReplayFixture.Parameters().Serialize(writer);
        memory.SetLength(memory.Length - 3);
        memory.Position = memory.Length;
        writer.Write(0x12345678);
        using var array = FixedArray<byte>.Alloc((int)memory.Length);
        memory.ToArray().CopyTo(array.Span);
        var stream = new FixedArrayStream(array);
        var read = new DrumsEngineParameters(ref stream, 25);
        Assert.That(read.EliteHiHatSustains, Is.Zero);
        Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }

    [Test]
    public void SustainFaultStatisticsPreserveCopyResetAndConsecutivePayloadBoundaries()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        for (int i = 0; i < 20; i++)
            new DrumsStats { HiHatSustainBreaks = i, Overhits = i }.Serialize(writer);
        writer.Write(0x12345678);
        using var array = FixedArray<byte>.Alloc((int)memory.Length);
        memory.ToArray().CopyTo(array.Span);
        var stream = new FixedArrayStream(array);
        for (int i = 0; i < 20; i++)
        {
            var read = new DrumsStats(ref stream, ReplayIO.REPLAY_VERSIONS.CURRENT);
            Assert.That(read.HiHatSustainBreaks, Is.EqualTo(i));
            Assert.That(new DrumsStats(read).HiHatSustainBreaks, Is.EqualTo(i));
            read.Reset();
            Assert.That(read.HiHatSustainBreaks, Is.Zero);
        }
        Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }
}
