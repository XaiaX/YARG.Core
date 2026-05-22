using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

[TestFixture]
public sealed class PartyVocalsEngineTests
{
    // Pitch window: perfect <= 0.5 semitones, total window = 1.5 semitones
    private static readonly VocalsEngineParameters EngineParameters = new(
        new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0),
        4,
        VocalsEngineTests.StarMultiplierThresholds,
        VocalsEngineTests.SoloBonusStarMultiplierThresholds,
        1.5f,       // pitchWindow
        0.5f,       // pitchWindowPerfect
        0.75,       // phraseHitPercent
        60.0,       // approximateVocalFps
        true,       // singToActivateStarPower
        1000);      // pointsPerPhrase

    // Cached reflection accessors for YargFreeVocalsEngine
    private static readonly MethodInfo CanVocalNoteBeHitMethod =
        typeof(YargFreeVocalsEngine).GetMethod("CanVocalNoteBeHit",
            BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find CanVocalNoteBeHit on YargFreeVocalsEngine");

    private static readonly PropertyInfo PitchSangProperty =
        typeof(VocalsEngine).GetProperty("PitchSang",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find PitchSang property");

    private static readonly PropertyInfo CurrentTimeProperty =
        typeof(YargFreeVocalsEngine).BaseType.BaseType.GetProperty("CurrentTime",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find CurrentTime property");

    private static readonly FieldInfo MicPitchesField =
        typeof(YargFreeVocalsEngine).GetField("_micPitches",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException("Could not find _micPitches field");

    private static readonly FieldInfo MicPartHitsField =
        typeof(YargFreeVocalsEngine).GetField("_micPartHits",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException("Could not find _micPartHits field");

    private static readonly MethodInfo GetMicPartHitMethod =
        typeof(YargFreeVocalsEngine).GetMethod("GetMicPartHit",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException("Could not find GetMicPartHit method");

    // ================================================================
    // AC3.1: Multi-mic engine construction with micCount parameter
    // ================================================================
    [Test]
    public void Constructor_MicCount_AllocatesPerMicState()
    {
        // Test micCount = 3
        var parts = Create3Parts();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, new SyncTrack(480), EngineParameters, false, micCount: 3);

        var micPitches = (float[])MicPitchesField.GetValue(engine)!;
        var micPartHits = (double[,])MicPartHitsField.GetValue(engine)!;

        Assert.Multiple(() =>
        {
            Assert.That(micPitches.Length, Is.EqualTo(3), "Should allocate _micPitches array of size 3");
            Assert.That(micPartHits.GetLength(0), Is.EqualTo(3), "Should allocate _micPartHits with 3 rows");
            Assert.That(micPartHits.GetLength(1), Is.EqualTo(3), "Should allocate _micPartHits with 3 columns for 3 parts");
        });

        // Test micCount = 1
        var engine1 = new YargFreeVocalsEngine(primaryChart, parts, new SyncTrack(480), EngineParameters, false, micCount: 1);
        var micPitches1 = (float[])MicPitchesField.GetValue(engine1)!;
        var micPartHits1 = (double[,])MicPartHitsField.GetValue(engine1)!;
        Assert.Multiple(() =>
        {
            Assert.That(micPitches1.Length, Is.EqualTo(1), "Should allocate _micPitches array of size 1");
            Assert.That(micPartHits1.GetLength(0), Is.EqualTo(1), "Should allocate _micPartHits with 1 row");
            Assert.That(micPartHits1.GetLength(1), Is.EqualTo(3), "Should allocate _micPartHits with 3 columns for 3 parts");
        });

        // Test micCount = 7 (maximum)
        var engine7 = new YargFreeVocalsEngine(primaryChart, parts, new SyncTrack(480), EngineParameters, false, micCount: 7);
        var micPitches7 = (float[])MicPitchesField.GetValue(engine7)!;
        var micPartHits7 = (double[,])MicPartHitsField.GetValue(engine7)!;
        Assert.Multiple(() =>
        {
            Assert.That(micPitches7.Length, Is.EqualTo(7), "Should allocate _micPitches array of size 7");
            Assert.That(micPartHits7.GetLength(0), Is.EqualTo(7), "Should allocate _micPartHits with 7 rows");
            Assert.That(micPartHits7.GetLength(1), Is.EqualTo(3), "Should allocate _micPartHits with 3 columns for 3 parts");
        });
    }

    // ================================================================
    // AC3.1 (failure case): micCount out of range throws
    // ================================================================
    [Test]
    public void Constructor_MicCountOutOfRange_Throws()
    {
        var parts = Create3Parts();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();

        // Test micCount = 0 (below minimum)
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            new YargFreeVocalsEngine(primaryChart, parts, new SyncTrack(480), EngineParameters, false, micCount: 0);
        }, "Should throw for micCount = 0");

        // Test micCount = 8 (above maximum)
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            new YargFreeVocalsEngine(primaryChart, parts, new SyncTrack(480), EngineParameters, false, micCount: 8);
        }, "Should throw for micCount = 8");
    }

    // ================================================================
    // AC3.2: Single mic constructor behaves identically to previous version
    // ================================================================
    [Test]
    public void SingleMicConstructor_BehavesIdenticallyToPreviousVersion()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),
            CreateVocalsPart(isHarmony: true),
        };

        AddPhraseWithPitch(parts[0], 60, tickOffset: 0);
        AddPhraseWithPitch(parts[1], 64, tickOffset: 0);

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = new SyncTrack(480);

        // Create engines using both constructors
        var legacyEngine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false);
        var newEngine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 1);

        // Process a sequence of inputs
        var testPitches = new[] { 60f, 64f, 60f, 62f, 66f }; // C4, E4, C4, C#4, F#4
        foreach (var pitch in testPitches)
        {
            var input = GameInput.Create(0.0, VocalsAction.Pitch, pitch);
            legacyEngine.QueueInput(ref input);
            newEngine.QueueInput(ref input);
        }

        legacyEngine.Update(1.5);
        newEngine.Update(1.5);

        // Verify that both engines behave identically
        Assert.Multiple(() =>
        {
            Assert.That(legacyEngine.PhraseTicksHit, Is.EqualTo(newEngine.PhraseTicksHit), "PhraseTicksHit should match");
            Assert.That(legacyEngine.CurrentTargetHarmonyIndex, Is.EqualTo(newEngine.CurrentTargetHarmonyIndex), "CurrentTargetHarmonyIndex should match");
        });
    }

    // ================================================================
    // AC4.1: SetMicPitch with matching note accumulates proportional to ticks since last
    // ================================================================
    [Test]
    public void SetMicPitch_MatchingNote_AccumulatesProportionalToTicksSinceLast()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),  // HARM1 at C4 = 60
            CreateVocalsPart(isHarmony: true),   // HARM2 at E4 = 64
        };

        AddLongPhraseWithPitch(parts[0], 60, tickOffset: 0);
        AddLongPhraseWithPitch(parts[1], 64, tickOffset: 0);

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        var micPartHits = (double[,])MicPartHitsField.GetValue(engine)!;

        Assert.That(micPartHits[0, 0], Is.EqualTo(0), "Should start at 0");

        // First update past the countdown boundary, then set pitch and update again.
        // The engine has queued updates from BuildCountdownsFromAllParts that fire
        // before our target time, so SetMicPitch must be called after those consume.
        engine.Update(0.1);
        engine.SetMicPitch(0, 60f);
        engine.Update(0.25);

        var mic0Part0 = micPartHits[0, 0];
        var mic0Part1 = micPartHits[0, 1];

        Assert.Multiple(() =>
        {
            Assert.That(mic0Part0, Is.GreaterThan(0), "Mic 0 HARM1 should accumulate when singing C4");
            Assert.That(mic0Part0, Is.LessThan(500.0), "Accumulation should be bounded");
            Assert.That(mic0Part1, Is.EqualTo(0), "Mic 0 HARM2 should not accumulate when singing C4");
        });
    }

    // ================================================================
    // AC4.2: SetMicPitch with unison match accumulates both parts
    // ================================================================
    [Test]
    public void SetMicPitch_UnisonMatch_AccumulatesBothParts()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),  // HARM1 at C4 = 60
            CreateVocalsPart(isHarmony: true),   // HARM2 also at C4 = 60 (unison)
        };

        AddLongPhraseWithPitch(parts[0], 60, tickOffset: 0);
        AddLongPhraseWithPitch(parts[1], 60, tickOffset: 0);

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        var micPartHits = (double[,])MicPartHitsField.GetValue(engine)!;

        // Advance past countdown, then set pitch and update
        engine.Update(0.1);
        engine.SetMicPitch(0, 60f);
        engine.Update(0.25);

        var mic0Part0 = micPartHits[0, 0];
        var mic0Part1 = micPartHits[0, 1];

        Assert.Multiple(() =>
        {
            Assert.That(mic0Part0, Is.GreaterThan(0), "Mic 0 HARM1 should accumulate for unison");
            Assert.That(mic0Part1, Is.GreaterThan(0), "Mic 0 HARM2 should accumulate for unison");
            Assert.That(mic0Part0, Is.EqualTo(mic0Part1), "Both parts should accumulate equally for unison");
        });
    }

    // ================================================================
    // AC4.3: SetMicPitch with no match accumulates nothing
    // ================================================================
    [Test]
    public void SetMicPitch_NoMatch_AccumulatesNothing()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),  // HARM1 at C4 = 60
            CreateVocalsPart(isHarmony: true),   // HARM2 at E4 = 64
        };

        AddLongPhraseWithPitch(parts[0], 60, tickOffset: 0);
        AddLongPhraseWithPitch(parts[1], 64, tickOffset: 0);

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        var micPartHits = (double[,])MicPartHitsField.GetValue(engine)!;

        // Advance past countdown, then set pitch and update
        engine.Update(0.1);
        engine.SetMicPitch(0, 66f);
        engine.Update(0.25);

        Assert.Multiple(() =>
        {
            Assert.That(micPartHits[0, 0], Is.EqualTo(0), "No accumulation for non-matching pitch on HARM1");
            Assert.That(micPartHits[0, 1], Is.EqualTo(0), "No accumulation for non-matching pitch on HARM2");
        });
    }

    // ================================================================
    // AC4.4: Two mics with same pitch accumulate independently per mic
    // ================================================================
    [Test]
    public void SetMicPitch_TwoMicsSamePitch_PerMicRowsIncrementIndependently()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),  // HARM1 at C4 = 60
        };

        AddLongPhraseWithPitch(parts[0], 60, tickOffset: 0);

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        // Advance past countdown, then set both mics and update
        engine.Update(0.1);
        engine.SetMicPitch(0, 60f);
        engine.SetMicPitch(1, 60f);
        engine.Update(0.25);

        var mic0Part0 = (double)GetMicPartHitMethod.Invoke(engine, new object[] { 0, 0 })!;
        var mic1Part0 = (double)GetMicPartHitMethod.Invoke(engine, new object[] { 1, 0 })!;

        Assert.Multiple(() =>
        {
            Assert.That(mic0Part0, Is.GreaterThan(0), "Mic 0 should accumulate");
            Assert.That(mic1Part0, Is.GreaterThan(0), "Mic 1 should accumulate");
            Assert.That(mic0Part0, Is.EqualTo(mic1Part0), "Both mics accumulate independently and equally for same pitch");
        });
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static List<VocalsPart> Create3Parts()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),
            CreateVocalsPart(isHarmony: true),
            CreateVocalsPart(isHarmony: true),
        };

        AddPhraseWithPitch(parts[0], 60, tickOffset: 0);
        AddPhraseWithPitch(parts[1], 64, tickOffset: 0);
        AddPhraseWithPitch(parts[2], 67, tickOffset: 0);

        return parts;
    }

    private static VocalsPart CreateVocalsPart(bool isHarmony)
    {
        return new VocalsPart(isHarmony, new(), new(), new(), new());
    }

    private static void AddPhraseWithPitch(VocalsPart part, int midiPitch, uint tickOffset)
    {
        var note = new VocalNote(NoteFlags.None, false, 0.0, 1.0, tickOffset, 480);
        var lyricNote = new VocalNote(midiPitch, 0, VocalNoteType.Lyric, 0.0, 0.5, tickOffset, 240);
        note.AddChildNote(lyricNote);
        var lyrics = new List<LyricEvent>
        {
            new LyricEvent(LyricSymbolFlags.None, "Test", 0.0, tickOffset)
        };
        part.NotePhrases.Add(new VocalsPhrase(0.0, 1.0, tickOffset, 480, note, lyrics));
    }

    private static void AddLongPhraseWithPitch(VocalsPart part, int midiPitch, uint tickOffset)
    {
        var note = new VocalNote(NoteFlags.None, false, 0.0, 2.0, tickOffset, 960);
        var lyricNote = new VocalNote(midiPitch, 0, VocalNoteType.Lyric, 0.0, 1.0, tickOffset, 480);
        note.AddChildNote(lyricNote);
        var lyrics = new List<LyricEvent>
        {
            new LyricEvent(LyricSymbolFlags.None, "Test", 0.0, tickOffset)
        };
        part.NotePhrases.Add(new VocalsPhrase(0.0, 2.0, tickOffset, 960, note, lyrics));
    }

    /// <summary>
    /// Creates a SyncTrack with 120 BPM tempo at tick 0 so TimeToTick returns meaningful values.
    /// Without a tempo entry, TimeToTick always returns 0, making accumulation impossible.
    /// </summary>
    private static SyncTrack CreateSyncTrackWithTempo()
    {
        var syncTrack = new SyncTrack(480);
        syncTrack.Tempos.Add(new TempoChange(120.0, 0.0, 0));
        return syncTrack;
    }

    /// <summary>
    /// Invokes the engine's real CanVocalNoteBeHit method via reflection.
    /// Sets PitchSang and CurrentTime on the engine instance before calling.
    /// </summary>
    private static (bool hit, float hitPercent) InvokeCanVocalNoteBeHit(
        YargFreeVocalsEngine engine, VocalNote note, float sungPitch)
    {
        // Set PitchSang (protected setter on VocalsEngine)
        PitchSangProperty.SetValue(engine, sungPitch);

        // Set CurrentTime so note.PitchAtSongTime returns the note's pitch.
        // At time 0, a note at time 0 with timeLength > 0 returns its Pitch.
        CurrentTimeProperty.SetValue(engine, 0.0);

        // Call CanVocalNoteBeHit(note, out float hitPercent)
        var hitPercent = new object[2];
        hitPercent[0] = note;
        hitPercent[1] = 0f; // default

        var result = (bool)CanVocalNoteBeHitMethod.Invoke(engine, hitPercent)!;

        return (result, (float)hitPercent[1]);
    }
}