using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

public sealed class VocalsEngineTests
{

    public static float[] StarMultiplierThresholds { get; } =
    {
        0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f
    };

    public static float[] SoloBonusStarMultiplierThresholds = {
        0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f
    };

    private static readonly VocalsEngineParameters EngineParameters = new(
        new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0, 0),
        4,
        StarMultiplierThresholds,
        SoloBonusStarMultiplierThresholds,
        1.5f,
        0.5f,
        0.75,
        60.0,
        true,
        1000);

    [Test]
    public void GetNoteInPhraseAtSongTick_ReturnsMatchingLyric_AndSkipsPercussion()
    {
        var engine = CreateEngine(out var phrase, out var firstLyric, out _, out var secondLyric);

        Assert.That(engine.GetNoteAtTick(phrase, 120), Is.SameAs(firstLyric));
        Assert.That(engine.GetNoteAtTick(phrase, 300), Is.Null);
        Assert.That(engine.GetNoteAtTick(phrase, 600), Is.SameAs(secondLyric));
    }

    [Test]
    public void GetNoteInPhraseAtSongTick_PrefersCarriedNote()
    {
        var engine = CreateEngine(out var phrase, out _, out _, out _);
        var carried = new VocalNote(67, 0, VocalNoteType.Lyric, 1.5, 0.5, 720, 240);

        engine.SetCarriedNote(carried);

        Assert.That(engine.GetNoteAtTick(phrase, 800), Is.SameAs(carried));
    }

    [Test]
    public void GetNoteInPhraseAtSongTick_DoesNotAllocate_AfterWarmup()
    {
        var engine = CreateEngine(out var phrase, out _, out _, out _);

        _ = engine.GetNoteAtTick(phrase, 120);
        _ = engine.GetNoteAtTick(phrase, 600);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int hits = 0;

        for (int i = 0; i < 10_000; i++)
        {
            var note = engine.GetNoteAtTick(phrase, i % 2 == 0 ? 120u : 600u);
            if (note != null)
            {
                hits++;
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(hits, Is.EqualTo(10_000));
        Assert.That(allocated, Is.EqualTo(0));
    }

    [TestCase(60f, 16.0, true)]
    [TestCase(60.5f, 16.0, true)]
    [TestCase(61f, 8.0, true)]
    [TestCase(61.5f, 0.0, true)]
    [TestCase(61.6f, 0.0, false)]
    [TestCase(48f, 16.0, true)]
    [TestCase(72f, 16.0, true)]
    public void PitchInput_CreditsPitchWindowAndOctave(float pitch, double expectedTicks, bool expectedHit)
    {
        var engine = CreateScoringEngine(out _);
        bool? hit = null;
        engine.OnHit += value => hit = value;

        Sing(engine, 0.05, pitch);

        Assert.That(hit, Is.EqualTo(expectedHit));
        Assert.That(engine.PhraseTicksTotal, Is.EqualTo(96u));
        Assert.That(engine.PhraseTicksHit, Is.EqualTo(expectedTicks).Within(0.0001));
    }

    [Test]
    public void PitchInput_OnSameTick_DoesNotCreditTwice()
    {
        var engine = CreateScoringEngine(out _);

        Sing(engine, 0.05, 60f);
        double firstCredit = engine.PhraseTicksHit;
        Sing(engine, 0.05, 60f);

        Assert.That(firstCredit, Is.EqualTo(16.0));
        Assert.That(engine.PhraseTicksHit, Is.EqualTo(firstCredit));
        Assert.That(engine.LastSingTick, Is.EqualTo(48u));
    }

    [TestCase(false, 0.5, 48u, 48u, 500)]
    [TestCase(true, 0.75, 96u, 0u, 1000)]
    public void PhraseEnd_AppliesHitThreshold(bool singSecondTime, double expectedPercent,
        uint expectedTicksHit, uint expectedTicksMissed, int expectedScore)
    {
        var engine = CreateScoringEngine(out var phrase, approximateVocalFps: 20.0);
        double? reportedPercent = null;
        bool? fullPoints = null;
        engine.OnPhraseHit += (percent, full, last) =>
        {
            reportedPercent = percent;
            fullPoints = full;
            Assert.That(last, Is.True);
        };

        Sing(engine, 0.05, 60f);
        if (singSecondTime)
        {
            Sing(engine, 0.075, 60f);
        }
        engine.Update(0.101);

        Assert.That(reportedPercent, Is.EqualTo(expectedPercent / 0.75).Within(0.0001));
        Assert.That(fullPoints, Is.EqualTo(singSecondTime));
        Assert.That(phrase.WasHit, Is.EqualTo(singSecondTime));
        Assert.That(phrase.WasMissed, Is.EqualTo(!singSecondTime));
        Assert.That(engine.EngineStats.TicksHit, Is.EqualTo(expectedTicksHit));
        Assert.That(engine.EngineStats.TicksMissed, Is.EqualTo(expectedTicksMissed));
        Assert.That(engine.EngineStats.TotalScore, Is.EqualTo(expectedScore));
    }

    [Test]
    public void PhraseEnd_AtEndTickAcceptsPitch_AndResolvesOnlyAfterEndTick()
    {
        var engine = CreateScoringEngine(out var phrase, approximateVocalFps: 20.0);
        int results = 0;
        engine.OnPhraseHit += (_, _, _) => results++;

        Sing(engine, 0.1, 60f);

        Assert.That(engine.PhraseTicksHit, Is.EqualTo(48.0));
        Assert.That(phrase.WasHit, Is.False);
        Assert.That(phrase.WasMissed, Is.False);
        Assert.That(results, Is.Zero);

        Sing(engine, 0.101, 60f);

        Assert.That(engine.EngineStats.TicksHit, Is.EqualTo(48u));
        Assert.That(engine.EngineStats.TicksMissed, Is.EqualTo(48u));
        Assert.That(phrase.WasMissed, Is.True);
        Assert.That(results, Is.EqualTo(1));
    }

    private static TestVocalsEngine CreateScoringEngine(out VocalNote phrase, double approximateVocalFps = 60.0)
    {
        phrase = new VocalNote(NoteFlags.None, false, 0.0, 0.1, 0, 96);
        phrase.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 0.0, 0.1, 0, 96));
        var chart = new InstrumentDifficulty<VocalNote>(Instrument.Vocals, Difficulty.Expert,
            new() { phrase }, new(), new());
        var parameters = new VocalsEngineParameters(
            new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0, 0),
            4, StarMultiplierThresholds, SoloBonusStarMultiplierThresholds,
            1.5f, 0.5f, 0.75, approximateVocalFps, true, 1000);
        var syncTrack = new SyncTrack(480);
        syncTrack.Tempos.Add(new TempoChange(120, 0, 0));
        return new TestVocalsEngine(chart, syncTrack, parameters);
    }

    private static void Sing(TestVocalsEngine engine, double time, float pitch)
    {
        var input = GameInput.Create(time, VocalsAction.Pitch, pitch);
        engine.QueueInput(ref input);
        engine.Update(time);
    }

    private static TestVocalsEngine CreateEngine(out VocalNote phrase, out VocalNote firstLyric,
        out VocalNote percussion, out VocalNote secondLyric)
    {
        phrase = new VocalNote(NoteFlags.None, false, 0.0, 2.0, 0, 960);
        firstLyric = new VocalNote(60, 0, VocalNoteType.Lyric, 0.0, 0.5, 0, 240);
        percussion = new VocalNote(-1, 0, VocalNoteType.Percussion, 0.5, 0.25, 240, 120);
        secondLyric = new VocalNote(62, 0, VocalNoteType.Lyric, 1.0, 0.5, 480, 240);

        phrase.AddChildNote(firstLyric);
        phrase.AddChildNote(percussion);
        phrase.AddChildNote(secondLyric);

        var chart = new InstrumentDifficulty<VocalNote>(Instrument.Vocals, Difficulty.Expert,
            new() { phrase }, new(), new());

        return new TestVocalsEngine(chart, new SyncTrack(480), EngineParameters);
    }

    private sealed class TestVocalsEngine : YargVocalsEngine
    {
        public TestVocalsEngine(InstrumentDifficulty<VocalNote> chart, SyncTrack syncTrack,
            VocalsEngineParameters engineParameters)
            : base(chart, syncTrack, engineParameters, false)
        {
        }

        public VocalNote? GetNoteAtTick(VocalNote phrase, uint tick)
        {
            return GetNoteInPhraseAtSongTick(phrase, tick);
        }

        public void SetCarriedNote(VocalNote? note)
        {
            CarriedVocalNote = note;
        }
    }
}
