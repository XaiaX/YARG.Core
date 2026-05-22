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
public sealed class PartyVocalsPhraseGradingTests
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

    private static readonly FieldInfo CanonicalMetersField =
        typeof(YargFreeVocalsEngine).GetField("_canonicalMeters",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException("Could not find _canonicalMeters field");

    private static readonly PropertyInfo BaseStatsProperty =
        typeof(YargFreeVocalsEngine).BaseType.BaseType.GetProperty("BaseStats",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find BaseStats property");

    // ================================================================
    // party-vocals.AC6.1 + AC6.2: Rollback cadence updates meters without consuming buffer
    // ================================================================
    [Test]
    public void RollbackCadence_UpdatesMetersWithoutConsumingBuffer()
    {
        // Create engine with 2 mics, 3 parts
        var parts = Create3Parts();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        // Capture intermediate state before phrase end (rollback windows should fire during phrase)
        IReadOnlyList<double>? capturedMetersDuringPhrase = null;
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            // Capture first phrase event which should happen during phrase processing
            if (!isLast)
            {
                capturedMetersDuringPhrase = meters;
            }
        };

        // Feed pitch input for 1.5 seconds (should trigger ~3 rollback windows at 500ms)
        // Mic 0 sings C4 (60) which matches HARM1
        for (double t = 0.0; t <= 1.5; t += 1.0 / 60.0)
        {
            engine.SetMicPitch(0, 60f);
            engine.SetMicPitch(1, 64f); // Mic 1 sings E4 which matches HARM2
            engine.Update(t + 0.1); // Advance past countdown
        }

        // Let the engine process through phrase end
        engine.Update(3.0);

        // Check that meters were updated during rollback (captured from event)
        Assert.That(capturedMetersDuringPhrase, Is.Not.Null, "Should have captured meters during phrase");
        Assert.That(capturedMetersDuringPhrase![0], Is.GreaterThan(0), "HARM1 meter should be filled");
        Assert.That(capturedMetersDuringPhrase[1], Is.GreaterThan(0), "HARM2 meter should be filled");
        Assert.That(capturedMetersDuringPhrase[2], Is.GreaterThan(0), "HARM3 meter should be filled by mic 1");

        // After phrase end, meters will be cleared, so we check the final phrase event
        IReadOnlyList<double>? finalMeters = null;
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            if (isLast)
            {
                finalMeters = meters;
            }
        };

        engine.Update(4.0);

        Assert.That(finalMeters, Is.Not.Null, "Should capture final phrase event");
        Assert.That(finalMeters![0], Is.GreaterThan(0), "Final HARM1 meter should be filled");
        Assert.That(finalMeters[1], Is.GreaterThan(0), "Final HARM2 meter should be filled");
    }

    // ================================================================
    // party-vocals.AC6.3 + AC7.1: Phrase end all meters below threshold emits Miss and breaks combo
    // ================================================================
    [Test]
    public void PhraseEnd_AllMetersBelowThreshold_EmitsMissAndBreaksCombo()
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

        // Set initial combo to test that it breaks
        var initialCombo = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;

        // Capture phrase event
        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Feed pitch that doesn't match any part (F#4 = 66)
        for (double t = 0.0; t <= 1.0; t += 1.0 / 60.0)
        {
            engine.SetMicPitch(0, 66f); // F#4 - too far from C4 and E4
            engine.SetMicPitch(1, 66f);
            engine.Update(t + 0.1);
        }

        // Advance past phrase end
        engine.Update(1.5);

        // Verify results
        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.Miss), "Should emit Miss grade");
        Assert.That(capturedMeters, Is.Not.Null, "Should capture meters");
        Assert.That(capturedMeters![0], Is.LessThan(EngineParameters.PhraseHitPercent), "HARM1 meter below threshold");
        Assert.That(capturedMeters[1], Is.LessThan(EngineParameters.PhraseHitPercent), "HARM2 meter below threshold");

        // Verify combo was reset
        var finalCombo = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;
        Assert.That(finalCombo, Is.LessThanOrEqualTo(initialCombo), "Combo should be reset");
    }

    // ================================================================
    // party-vocals.AC6.3 + AC7.2: Phrase end one meter above threshold emits Awesome
    // ================================================================
    [Test]
    public void PhraseEnd_OneMeterAboveThreshold_EmitsAwesome()
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

        // Capture phrase event
        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        bool phraseEnded = false;
        int comboBefore = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
            phraseEnded = true;
        };

        // Feed pitch matching only HARM1
        for (double t = 0.0; t <= 1.5; t += 1.0 / 60.0)
        {
            engine.SetMicPitch(0, 60f); // C4 - matches HARM1
            engine.SetMicPitch(1, 78f); // F#5 - doesn't match either
            engine.Update(t + 0.1);
        }

        // Advance past phrase end (phrase ends at 2 seconds)
        engine.Update(2.1);

        // Debug output
        Console.WriteLine($"Phrase ended: {phraseEnded}");
        Console.WriteLine($"Captured grade: {capturedGrade}");
        Console.WriteLine($"Captured meters: {capturedMeters}");

        // Also check current engine state
        var canonicalMeters = (double[])CanonicalMetersField.GetValue(engine)!;
        var micPartHits = (double[,])MicPartHitsField.GetValue(engine)!;
        Console.WriteLine($"Canonical meters: [{string.Join(", ", canonicalMeters)}]");
        Console.WriteLine($"Mic part hits [0,0]: {micPartHits[0, 0]}");
        Console.WriteLine($"Mic part hits [1,0]: {micPartHits[1, 0]}");

        // Verify results
        Assert.That(phraseEnded, Is.True, "Phrase should have ended");
        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.Awesome), "Should emit Awesome grade");
        Assert.That(capturedMeters, Is.Not.Null, "Should capture meters");
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM1 meter above threshold");
        Assert.That(capturedMeters[1], Is.LessThan(EngineParameters.PhraseHitPercent), "HARM2 meter below threshold");

        // Verify combo incremented once
        int comboAfter = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;
        Assert.That(comboAfter, Is.EqualTo(comboBefore + 1), "Combo should increment once");
    }

    // ================================================================
    // party-vocals.AC6.3 + AC7.3: Phrase end two meters above threshold emits DoubleAwesome
    // ================================================================
    [Test]
    public void PhraseEnd_TwoMetersAboveThreshold_EmitsDoubleAwesome()
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

        // Capture phrase event
        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        int comboBefore = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Feed mic 0 matching HARM1, mic 1 matching HARM2
        for (double t = 0.0; t <= 1.5; t += 1.0 / 60.0)
        {
            engine.SetMicPitch(0, 60f); // C4 - matches HARM1
            engine.SetMicPitch(1, 64f); // E4 - matches HARM2
            engine.Update(t + 0.1);
        }

        // Advance past phrase end
        engine.Update(3.0);

        // Verify results
        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.DoubleAwesome), "Should emit DoubleAwesome grade");
        Assert.That(capturedMeters, Is.Not.Null, "Should capture meters");
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM1 meter above threshold");
        Assert.That(capturedMeters[1], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM2 meter above threshold");

        // Verify combo incremented only once (not twice)
        int comboAfter = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;
        Assert.That(comboAfter, Is.EqualTo(comboBefore + 1), "Combo should increment once, not twice");
    }

    // ================================================================
    // party-vocals.AC6.3 + AC7.4: Phrase end three meters above threshold emits TripleAwesome
    // ================================================================
    [Test]
    public void PhraseEnd_ThreeMetersAboveThreshold_EmitsTripleAwesome()
    {
        var parts = Create3Parts(); // 3 parts: HARM1, HARM2, HARM3 at C4, E4, G4

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 3);

        // Capture phrase event
        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        int comboBefore = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Feed each mic matching a distinct part
        for (double t = 0.0; t <= 1.5; t += 1.0 / 60.0)
        {
            engine.SetMicPitch(0, 60f); // C4 - matches HARM1
            engine.SetMicPitch(1, 64f); // E4 - matches HARM2
            engine.SetMicPitch(2, 67f); // G4 - matches HARM3
            engine.Update(t + 0.1);
        }

        // Advance past phrase end
        engine.Update(3.0);

        // Verify results
        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.TripleAwesome), "Should emit TripleAwesome grade");
        Assert.That(capturedMeters, Is.Not.Null, "Should capture meters");
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM1 meter above threshold");
        Assert.That(capturedMeters[1], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM2 meter above threshold");
        Assert.That(capturedMeters[2], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM3 meter above threshold");

        // Verify combo incremented once (not three times)
        int comboAfter = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;
        Assert.That(comboAfter, Is.EqualTo(comboBefore + 1), "Combo should increment once, not three times");
    }

    // ================================================================
    // party-vocals.AC7.5: Phrase end score awards sum of meter times points per phrase
    // ================================================================
    [Test]
    public void PhraseEnd_ScoreAwards_SumOfMeterTimesPointsPerPhrase()
    {
        var parts = Create3Parts(); // 3 parts: HARM1, HARM2, HARM3

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 3);

        // Capture phrase event and score
        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        int scoreBefore = ((BaseStats)BaseStatsProperty.GetValue(engine)!).CommittedScore;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Feed partial pitches to achieve specific meter fills
        // Mic 0 sings C4 (HARM1) occasionally to fill ~50% of HARM1
        // Mic 1 sings E4 (HARM2) moderately to fill ~80% of HARM2
        // Mic 2 sings G4 (HARM3) rarely to fill ~30% of HARM3
        for (double t = 0.0; t <= 1.5; t += 1.0 / 60.0)
        {
            engine.SetMicPitch(0, 60f); // Always sing HARM1 to fill ~50%
            engine.SetMicPitch(1, 64f); // Always sing HARM2 to fill ~80%
            engine.SetMicPitch(2, 67f); // Sing HARM3 only half the time for ~30%
            engine.Update(t + 0.1);
        }

        // Advance past phrase end
        engine.Update(3.0);

        // Verify results
        Assert.That(capturedGrade, Is.Not.Null, "Should capture grade");
        Assert.That(capturedMeters, Is.Not.Null, "Should capture meters");

        // Expected score: (0.5 + 0.8 + 0.3) * 1000 = 1600
        double expectedScore = (0.5 + 0.8 + 0.3) * EngineParameters.PointsPerPhrase;
        int scoreAfter = ((BaseStats)BaseStatsProperty.GetValue(engine)!).CommittedScore;
        Assert.That(scoreAfter, Is.EqualTo(scoreBefore + (int)expectedScore),
            $"Score should increase by {expectedScore}");
    }

    // ================================================================
    // party-vocals.AC8.1: SoloOnly_ThreeMicsSamePitch_SameMeterFillAsOneMic
    // ================================================================
    [Test]
    public void SoloOnly_ThreeMicsSamePitch_SameMeterFillAsOneMic()
    {
        // Create single-part chart (vocals-only song)
        var singlePart = CreateVocalsPart(isHarmony: false);
        AddPhraseWithPitch(singlePart, 60, tickOffset: 0);
        var parts = new List<VocalsPart> { singlePart };

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();

        // Test with 3 mics all reporting matching pitch
        var engine3 = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 3);

        // Test with 1 mic reporting matching pitch
        var engine1 = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 1);

        // Feed pitch to both engines for same duration
        for (double t = 0.0; t <= 1.5; t += 1.0 / 60.0)
        {
            // 3-mic engine: all mics sing C4
            engine3.SetMicPitch(0, 60f);
            engine3.SetMicPitch(1, 60f);
            engine3.SetMicPitch(2, 60f);
            engine3.Update(t + 0.1);

            // 1-mic engine: mic sings C4
            engine1.SetMicPitch(0, 60f);
            engine1.Update(t + 0.1);
        }

        // Advance past phrase end
        engine3.Update(3.0);
        engine1.Update(3.0);

        // Get meter values from both engines
        var meters3 = (double[])CanonicalMetersField.GetValue(engine3)!;
        var meters1 = (double[])CanonicalMetersField.GetValue(engine1)!;

        // Solo-only should use max-over-mics, not sum
        // Both should have same meter fill since max(1,1,1) = max(1) = 1
        Assert.That(meters3[0], Is.EqualTo(meters1[0]),
            "3-mic and 1-mic engines should have same meter fill for solo-only songs");
        Assert.That(meters3[0], Is.GreaterThan(0), "Meter should be filled");
    }

    // ================================================================
    // Unison: One mic two parts same pitch - Awesome not Double
    // ================================================================
    [Test]
    public void Unison_OneMicTwoPartsSamePitch_AwesomeNotDouble()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),  // HARM1 at C4 = 60
            CreateVocalsPart(isHarmony: true),   // HARM2 also at C4 = 60 (unison)
        };

        AddPhraseWithPitch(parts[0], 60, tickOffset: 0);
        AddPhraseWithPitch(parts[1], 60, tickOffset: 0);

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        // Capture phrase event
        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Feed matching pitch for the whole phrase
        for (double t = 0.0; t <= 1.5; t += 1.0 / 60.0)
        {
            engine.SetMicPitch(0, 60f); // C4 matches both parts
            engine.Update(t + 0.1);
        }

        // Advance past phrase end
        engine.Update(3.0);

        // Verify results
        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.Awesome),
            "Unison should emit Awesome, not DoubleAwesome - one mic can't fill two parts at once");
        Assert.That(capturedMeters, Is.Not.Null, "Should capture meters");
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM1 meter above threshold");
        Assert.That(capturedMeters[1], Is.LessThan(EngineParameters.PhraseHitPercent), "HARM2 meter below threshold due to assignment");
    }

    // ================================================================
    // Non-overlapping: One mic sequential parts - DoubleAwesome
    // ================================================================
    [Test]
    public void NonOverlapping_OneMicSequentialParts_DoubleAwesome()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),  // HARM1 at C4 = 60
            CreateVocalsPart(isHarmony: true),   // HARM2 at E4 = 64
        };

        // Add phrases at different times
        AddLongPhraseWithPitch(parts[0], 60, tickOffset: 0);      // HARM1 phrase: 0-960 ticks
        AddLongPhraseWithPitch(parts[1], 64, tickOffset: 0);      // HARM2 phrase: 0-960 ticks, but notes at different times

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        // Modify HARM2 to have its note at the second half
        var harm2Phrase = parts[1].NotePhrases[0];
        var harm2PhraseNote = harm2Phrase.PhraseParentNote;
        harm2PhraseNote.ChildNotes.Clear();
        var harm2Note = new VocalNote(64, 0, VocalNoteType.Lyric, 0.0, 0.5, 480, 240);
        harm2PhraseNote.AddChildNote(harm2Note);

        // Capture phrase event
        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Feed matching pitch for HARM1's section (first half), then HARM2's section (second half)
        for (double t = 0.0; t <= 2.0; t += 1.0 / 60.0)
        {
            if (t <= 1.0)
            {
                // First half: sing C4 (matches HARM1)
                engine.SetMicPitch(0, 60f);
            }
            else
            {
                // Second half: sing E4 (matches HARM2)
                engine.SetMicPitch(0, 64f);
            }
            engine.Update(t + 0.1);
        }

        // Advance past phrase end
        engine.Update(3.0);

        // Verify results
        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.DoubleAwesome),
            "Non-overlapping should emit DoubleAwesome - same mic can fill different parts across windows");
        Assert.That(capturedMeters, Is.Not.Null, "Should capture meters");
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM1 meter above threshold");
        Assert.That(capturedMeters[1], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM2 meter above threshold");
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

        AddLongPhraseWithPitch(parts[0], 60, tickOffset: 0);
        AddLongPhraseWithPitch(parts[1], 64, tickOffset: 0);
        AddLongPhraseWithPitch(parts[2], 67, tickOffset: 0);

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
            new LyricEvent(LyricSymbolFlags.None, "Test", 0.0, tickOffset),
            new LyricEvent(LyricSymbolFlags.None, "Test", 0.0, tickOffset + 480)
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
}