using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;

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

    private static SyncTrack Sync120()
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120.0, 0.0, 0));
        sync.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0.0, 0, 0, 0, 0, 0));
        return sync;
    }

    // 480 tpqn @ 120bpm => seconds = tick / 960.
    [Test]
    public void Bot_HitsPercussionNote_PurePhrase()
    {
        // Pure percussion phrase at t=5.0 (tick 4800); percussion child at the phrase start.
        var phrase = new VocalNote(NoteFlags.None, false, 5.0, 0.5, 4800, 480);
        var percussion = new VocalNote(-1, 0, VocalNoteType.Percussion, 5.0, 0.1, 4800, 96);
        phrase.AddChildNote(percussion);

        var chart = new InstrumentDifficulty<VocalNote>(Instrument.Vocals, Difficulty.Expert,
            new() { phrase }, new(), new());

        var engine = new YargVocalsEngine(chart, Sync120(), EngineParameters, isBot: true);
        int percussionHits = 0;
        engine.OnNoteHit += (_, note) => { if (note.IsPercussion) percussionHits++; };

        // Simulate frame-rate ticks across the percussion window.
        for (double t = 4.8; t <= 6.0; t += 1.0 / 60.0)
        {
            engine.Update(t);
        }

        Assert.That(percussionHits, Is.EqualTo(1),
            "A solo vocals bot should hit a percussion note in a pure percussion phrase.");
    }

    [Test]
    public void Bot_HitsPercussionNote_MixedPhrase()
    {
        // Mixed phrase: lyric (t=5.0) then percussion (t=5.5) then lyric (t=6.0).
        var phrase = new VocalNote(NoteFlags.None, false, 5.0, 1.5, 4800, 1440);
        var firstLyric = new VocalNote(60, 0, VocalNoteType.Lyric, 5.0, 0.4, 4800, 384);
        var percussion = new VocalNote(-1, 0, VocalNoteType.Percussion, 5.5, 0.1, 5280, 96);
        var secondLyric = new VocalNote(62, 0, VocalNoteType.Lyric, 6.0, 0.4, 5760, 384);
        phrase.AddChildNote(firstLyric);
        phrase.AddChildNote(percussion);
        phrase.AddChildNote(secondLyric);

        var chart = new InstrumentDifficulty<VocalNote>(Instrument.Vocals, Difficulty.Expert,
            new() { phrase }, new(), new());

        var engine = new YargVocalsEngine(chart, Sync120(), EngineParameters, isBot: true);
        int percussionHits = 0;
        engine.OnNoteHit += (_, note) => { if (note.IsPercussion) percussionHits++; };

        for (double t = 4.8; t <= 7.0; t += 1.0 / 60.0)
        {
            engine.Update(t);
        }

        Assert.That(percussionHits, Is.EqualTo(1),
            "A solo vocals bot should hit a percussion note in a mixed phrase.");
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
