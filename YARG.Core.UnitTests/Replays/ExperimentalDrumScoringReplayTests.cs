using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.IO;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Replays;

public sealed class ExperimentalDrumScoringReplayTests
{
    private static DrumsEngineParameters Parameters() =>
        EnginePreset.Default.Drums.Create(NativeEliteReplayFixture.Thresholds, NativeEliteReplayFixture.Thresholds,
            DrumsEngineParameters.DrumMode.Elite, lenientHiHatArticulation: true, alternateGhostScoring: true);

    private static EliteDrumNote Note(EliteDrumPad pad, double time, DrumNoteType type,
        EliteDrumsHatState hat = EliteDrumsHatState.Indifferent) =>
        new(pad, type, hat, EliteDrumsHatPedalType.Stomp, false, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumsChannelFlag.None, time, (uint)(time * 960), false);

    [Test]
    public void ReplayUsesRecordedPartialArticulationAndGhostMissRules()
    {
        var chart = NativeEliteReplayFixture.Chart();
        var track = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert, new()
        {
            Note(EliteDrumPad.Snare, .5, DrumNoteType.Neutral),
            Note(EliteDrumPad.HiHat, 1, DrumNoteType.Neutral, EliteDrumsHatState.Closed),
            Note(EliteDrumPad.Snare, 1.5, DrumNoteType.Ghost),
            Note(EliteDrumPad.Ride, 2, DrumNoteType.Neutral)
        }, new(), new());
        chart.EliteDrums.AddDifficulty(Difficulty.Expert, track);
        var parameters = Parameters();
        var engine = new EliteDrumsEngine(track.Clone(), chart.SyncTrack, parameters, false, true);
        var inputs = new[]
        {
            GameInput.Create(.5, EliteDrumsAction.EliteSnare, .8f),
            GameInput.Create(1, EliteDrumsAction.EliteOpenHiHat, .8f),
            GameInput.Create(2, EliteDrumsAction.EliteRide, .8f)
        };
        var live = NativeEliteReplayFixture.PlayLive(engine, inputs);
        var replay = NativeEliteReplayFixture.RoundTrip(NativeEliteReplayFixture.Data(
            NativeEliteReplayFixture.Profile(Instrument.EliteDrums), parameters, live, inputs));
        var restored = (DrumsEngineParameters) replay.Frames[0].EngineParameters;
        Assert.That(restored.LenientHiHatArticulation, Is.True);
        Assert.That(restored.AlternateGhostScoring, Is.True);
        Assert.That(live.MisarticulatedHiHats, Is.EqualTo(1));
        Assert.That(live.Combo, Is.EqualTo(2));
        Assert.That(live.NotesMissed, Is.EqualTo(1));
        Assert.That(live.IsFullCombo, Is.False);

        var result = NativeEliteReplayFixture.Analyze(chart, replay);
        NativeEliteReplayFixture.AssertLiveMatchesReplay(live, result);
        Assert.That(((DrumsStats) result.OriginalStats).MisarticulatedHiHats, Is.EqualTo(1));
        Assert.That(((DrumsStats) result.ResultStats).MisarticulatedHiHats, Is.EqualTo(1));
    }

    [Test]
    public void Version26DrumPayloadsDefaultToLegacyScoringAndKeepTheirBoundaries()
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        Parameters().Serialize(writer);
        memory.Position = memory.Length - 2;
        new DrumsStats { MisarticulatedHiHats = 3, HiHatSustainBreaks = 1 }.Serialize(writer);
        memory.SetLength(memory.Length - sizeof(int));
        memory.Position = memory.Length;
        writer.Write(0x12345678);
        using var bytes = FixedArray<byte>.Alloc((int)memory.Length);
        memory.ToArray().CopyTo(bytes.Span);
        var stream = new FixedArrayStream(bytes);
        var restored = new DrumsEngineParameters(ref stream, 26);
        var stats = new DrumsStats(ref stream, 26);
        Assert.That(restored.LenientHiHatArticulation, Is.False);
        Assert.That(restored.AlternateGhostScoring, Is.False);
        Assert.That(stats.MisarticulatedHiHats, Is.Zero);
        Assert.That(stats.HiHatSustainBreaks, Is.EqualTo(1));
        Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
    }
}
