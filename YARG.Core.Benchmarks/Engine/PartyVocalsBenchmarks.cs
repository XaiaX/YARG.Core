using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;

namespace YARG.Core.Benchmarks.Engine;

[MemoryDiagnoser]
public class PartyVocalsBenchmarks
{
    private YargFreeVocalsEngine _engine1Mic = null!;
    private YargFreeVocalsEngine _engine3Mics = null!;
    private YargFreeVocalsEngine _engine7Mics = null!;

    [GlobalSetup]
    public void Setup()
    {
        var parts = Create3PartChart();
        var syncTrack = CreateSyncTrack();
        var engineParams = CreateEngineParams();

        _engine1Mic = CreateEngine(parts, syncTrack, engineParams, 1);
        _engine3Mics = CreateEngine(parts, syncTrack, engineParams, 3);
        _engine7Mics = CreateEngine(parts, syncTrack, engineParams, 7);
    }

    [Benchmark(Baseline = true)]
    public void EngineUpdate_1Mic()
    {
        RunSingleTick(_engine1Mic, 1);
    }

    [Benchmark]
    public void EngineUpdate_3Mics()
    {
        RunSingleTick(_engine3Mics, 3);
    }

    [Benchmark]
    public void EngineUpdate_7Mics()
    {
        RunSingleTick(_engine7Mics, 7);
    }

    [Benchmark]
    public void ComputeBestAssignment_WorstCase_7Mics_3Parts()
    {
        double[,] hits = new double[7, 3];
        for (int i = 0; i < 7; i++)
            for (int j = 0; j < 3; j++)
                hits[i, j] = (i + j) * 0.1;

        uint[] totals = { 1000, 1000, 1000 };
        YargFreeVocalsEngine.ComputeBestAssignment(hits, totals, 0.6);
    }

    private static void RunSingleTick(YargFreeVocalsEngine engine, int micCount)
    {
        for (int i = 0; i < micCount; i++)
        {
            engine.SetMicPitch(i, 60f + i);
        }
    }

    private static List<VocalsPart> Create3PartChart()
    {
        var parts = new List<VocalsPart>
        {
            new(false, new(), new(), new(), new()),
            new(true, new(), new(), new(), new()),
            new(true, new(), new(), new(), new()),
        };

        // Add a long phrase to each part
        AddPhrase(parts[0], 0, 1920, 60);
        AddPhrase(parts[1], 0, 1920, 64);
        AddPhrase(parts[2], 0, 1920, 67);

        return parts;
    }

    private static void AddPhrase(VocalsPart part, uint tickOffset, uint tickLength, int midiPitch)
    {
        var note = new VocalNote(NoteFlags.None, false, 0.0, 4.0, tickOffset, tickLength);
        var lyricNote = new VocalNote(midiPitch, 0, VocalNoteType.Lyric, 0.0, 2.0, tickOffset, tickLength / 2);
        note.AddChildNote(lyricNote);
        var lyrics = new List<LyricEvent> { new(LyricSymbolFlags.None, "La", 0.0, tickOffset) };
        part.NotePhrases.Add(new VocalsPhrase(0.0, 4.0, tickOffset, tickLength, note, lyrics));
    }

    private static SyncTrack CreateSyncTrack()
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120.0, 0.0, 0));
        return sync;
    }

    private static VocalsEngineParameters CreateEngineParams()
    {
        return new VocalsEngineParameters(
            new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0),
            4,
            new float[] { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f },
            new float[] { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f },
            1.5f, 0.5f, 0.75, 60.0, true, 1000);
    }

    private static YargFreeVocalsEngine CreateEngine(
        List<VocalsPart> parts, SyncTrack syncTrack, VocalsEngineParameters engineParams, int micCount)
    {
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        return new YargFreeVocalsEngine(primaryChart, parts, syncTrack, engineParams, false, micCount: micCount);
    }
}
