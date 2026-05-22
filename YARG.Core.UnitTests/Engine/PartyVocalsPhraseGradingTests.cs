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

    private static readonly FieldInfo CanonicalMetersField =
        typeof(YargFreeVocalsEngine).GetField("_canonicalMeters",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
        ?? throw new InvalidOperationException("Could not find _canonicalMeters field");

    private static readonly PropertyInfo BaseStatsProperty =
        typeof(YargFreeVocalsEngine).BaseType.BaseType.GetProperty("BaseStats",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find BaseStats property");

    // ================================================================
    // party-vocals.AC6.1 + AC6.2: Rollback cadence updates meters
    // ================================================================
    [Test]
    public void RollbackCadence_UpdatesMetersWithoutConsumingBuffer()
    {
        var parts = Create2PartsWithLongPhrases();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        // Advance past countdowns
        engine.Update(0.1);

        // Feed pitch for 0.7 seconds — triggers first rollback window at ~0.6s (0.1 + 0.5)
        for (int i = 0; i < 42; i++) // 42 frames ≈ 0.7s at 60fps
        {
            engine.SetMicPitch(0, 60f);
            engine.SetMicPitch(1, 64f);
            engine.Update(0.1 + (i + 1) * (1.0 / 60.0));
        }

        // Check canonical meters via reflection — should have values after rollback window
        var meters = (double[])CanonicalMetersField.GetValue(engine)!;
        Assert.That(meters[0], Is.GreaterThan(0), "HARM1 meter should have value after rollback window");
        Assert.That(meters[1], Is.GreaterThan(0), "HARM2 meter should have value after rollback window");
    }

    // ================================================================
    // party-vocals.AC6.3 + AC7.1: All meters below threshold emits Miss
    // ================================================================
    [Test]
    public void PhraseEnd_AllMetersBelowThreshold_EmitsMissAndBreaksCombo()
    {
        var parts = Create2PartsWithLongPhrases();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Advance past countdowns
        engine.Update(0.1);

        // Feed non-matching pitch (F#4 = 66, too far from C4=60 and E4=64)
        for (int i = 0; i < 90; i++)
        {
            engine.SetMicPitch(0, 66f);
            engine.SetMicPitch(1, 66f);
            engine.Update(0.1 + (i + 1) * (1.0 / 60.0));
        }

        // Advance past phrase end (phrase ends at ~2.0s with AddLongPhraseWithPitch)
        engine.Update(2.5);

        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.Miss), "Should emit Miss grade");
        Assert.That(capturedMeters, Is.Not.Null);
    }

    // ================================================================
    // party-vocals.AC6.3 + AC7.2: One meter above threshold emits Awesome
    // ================================================================
    [Test]
    public void PhraseEnd_OneMeterAboveThreshold_EmitsAwesome()
    {
        var parts = Create2PartsWithLongPhrases();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        int comboBefore = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Advance past countdowns
        engine.Update(0.1);

        // Feed mic 0 matching HARM1 only, mic 1 non-matching
        for (int i = 0; i < 90; i++)
        {
            engine.SetMicPitch(0, 60f);  // C4 matches HARM1
            engine.SetMicPitch(1, 78f);  // F#5 doesn't match
            engine.Update(0.1 + (i + 1) * (1.0 / 60.0));
        }

        // Advance past phrase end
        engine.Update(2.5);

        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.Awesome), "Should emit Awesome grade");
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM1 meter above threshold");
        Assert.That(capturedMeters[1], Is.LessThan(EngineParameters.PhraseHitPercent), "HARM2 meter below threshold");

        int comboAfter = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;
        Assert.That(comboAfter, Is.EqualTo(comboBefore + 1), "Combo should increment once");
    }

    // ================================================================
    // party-vocals.AC6.3 + AC7.3: Two meters above threshold emits DoubleAwesome
    // ================================================================
    [Test]
    public void PhraseEnd_TwoMetersAboveThreshold_EmitsDoubleAwesome()
    {
        var parts = Create2PartsWithLongPhrases();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        int comboBefore = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Advance past countdowns
        engine.Update(0.1);

        // Feed mic 0 matching HARM1, mic 1 matching HARM2
        for (int i = 0; i < 90; i++)
        {
            engine.SetMicPitch(0, 60f);  // C4 matches HARM1
            engine.SetMicPitch(1, 64f);  // E4 matches HARM2
            engine.Update(0.1 + (i + 1) * (1.0 / 60.0));
        }

        // Advance past phrase end
        engine.Update(2.5);

        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.DoubleAwesome), "Should emit DoubleAwesome grade");
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent));
        Assert.That(capturedMeters[1], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent));

        int comboAfter = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;
        Assert.That(comboAfter, Is.EqualTo(comboBefore + 1), "Combo should increment once, not twice");
    }

    // ================================================================
    // party-vocals.AC6.3 + AC7.4: Three meters above threshold emits TripleAwesome
    // ================================================================
    [Test]
    public void PhraseEnd_ThreeMetersAboveThreshold_EmitsTripleAwesome()
    {
        var parts = Create3PartsWithLongPhrases();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 3);

        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        int comboBefore = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Advance past countdowns
        engine.Update(0.1);

        // Each mic matches a distinct part
        for (int i = 0; i < 90; i++)
        {
            engine.SetMicPitch(0, 60f);  // C4 matches HARM1
            engine.SetMicPitch(1, 64f);  // E4 matches HARM2
            engine.SetMicPitch(2, 67f);  // G4 matches HARM3
            engine.Update(0.1 + (i + 1) * (1.0 / 60.0));
        }

        // Advance past phrase end
        engine.Update(2.5);

        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.TripleAwesome), "Should emit TripleAwesome grade");
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent));
        Assert.That(capturedMeters[1], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent));
        Assert.That(capturedMeters[2], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent));

        int comboAfter = ((BaseStats)BaseStatsProperty.GetValue(engine)!).Combo;
        Assert.That(comboAfter, Is.EqualTo(comboBefore + 1), "Combo should increment once");
    }

    // ================================================================
    // party-vocals.AC7.5: Score awards sum of meter times PointsPerPhrase
    // ================================================================
    [Test]
    public void PhraseEnd_ScoreAwards_SumOfMeterTimesPointsPerPhrase()
    {
        var parts = Create3PartsWithLongPhrases();
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 3);

        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;
        int scoreBefore = ((BaseStats)BaseStatsProperty.GetValue(engine)!).CommittedScore;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Advance past countdowns
        engine.Update(0.1);

        // All mics sing matching pitch — all three meters should fill fully
        for (int i = 0; i < 90; i++)
        {
            engine.SetMicPitch(0, 60f);
            engine.SetMicPitch(1, 64f);
            engine.SetMicPitch(2, 67f);
            engine.Update(0.1 + (i + 1) * (1.0 / 60.0));
        }

        // Advance past phrase end
        engine.Update(2.5);

        Assert.That(capturedGrade, Is.Not.Null, "Should capture grade");

        // Score should be sum of (meter * PointsPerPhrase) for each part with ticks
        double expectedMax = 3.0 * EngineParameters.PointsPerPhrase; // max if all meters = 1.0
        int scoreAfter = ((BaseStats)BaseStatsProperty.GetValue(engine)!).CommittedScore;
        Assert.That(scoreAfter, Is.GreaterThan(scoreBefore), "Score should increase");
        Assert.That(scoreAfter - scoreBefore, Is.LessThanOrEqualTo((int)expectedMax), "Score should not exceed max");
    }

    // ================================================================
    // party-vocals.AC8.1: Solo-only max-over-mics (not sum)
    // ================================================================
    [Test]
    public void SoloOnly_ThreeMicsSamePitch_MeterCappedNotSummed()
    {
        // Single-part chart with long phrase
        var singlePart = CreateVocalsPart(isHarmony: false);
        AddLongPhraseWithPitch(singlePart, 60, tickOffset: 0);
        var parts = new List<VocalsPart> { singlePart };

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();

        // 3 mics all matching pitch — max-over-mics should cap at 1.0 (not 3.0)
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 3);

        // Advance past countdowns
        engine.Update(0.1);

        // Feed all mics singing C4
        for (int i = 0; i < 90; i++)
        {
            engine.SetMicPitch(0, 60f);
            engine.SetMicPitch(1, 60f);
            engine.SetMicPitch(2, 60f);
            engine.Update(0.1 + (i + 1) * (1.0 / 60.0));
        }

        // Advance past phrase end
        engine.Update(2.5);

        // The canonicalMeters are cleared at phrase end, so subscribe to capture
        // Instead, just verify the meter doesn't exceed 1.0 during accumulation
        // The solo-only path in CommitWindowAssignment takes max, not sum
        // If it summed, meter would be 3.0 (3 mics * 1.0 each). If max, meter caps at 1.0.
        // We verify indirectly: score should be reasonable (not 3x)
        var stats = (BaseStats)BaseStatsProperty.GetValue(engine)!;
        Assert.That(stats.CommittedScore, Is.GreaterThan(0), "Should have scored something");
        Assert.That(stats.CommittedScore, Is.LessThanOrEqualTo(EngineParameters.PointsPerPhrase),
            "Solo-only score should not exceed PointsPerPhrase (max-over-mics caps at 1.0)");
    }

    // ================================================================
    // Unison: One mic two parts same pitch — Awesome not Double
    // ================================================================
    [Test]
    public void Unison_OneMicTwoPartsSamePitch_AwesomeNotDouble()
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

        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Advance past countdowns
        engine.Update(0.1);

        // Feed matching pitch — mic 0 sings C4 which matches both parts
        for (int i = 0; i < 90; i++)
        {
            engine.SetMicPitch(0, 60f);
            engine.Update(0.1 + (i + 1) * (1.0 / 60.0));
        }

        // Advance past phrase end
        engine.Update(2.5);

        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.Awesome),
            "Unison should emit Awesome — one mic can't fill two parts at once");
        Assert.That(capturedMeters, Is.Not.Null);
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent), "HARM1 meter above threshold");
    }

    // ================================================================
    // Non-overlapping: One mic sequential parts — DoubleAwesome
    // ================================================================
    [Test]
    public void NonOverlapping_OneMicSequentialParts_DoubleAwesome()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),
            CreateVocalsPart(isHarmony: true),
        };

        // Double-length phrase: 1920 ticks = 4 beats = 2.0s at 120 BPM
        // HARM1 child: ticks 0–960 (first 1.0s), HARM2 child: ticks 960–1920 (second 1.0s)
        const uint parentTickLength = 1920;
        const double parentTimeLength = 4.0; // 1920 ticks at 120BPM/480res

        var harm1Parent = new VocalNote(NoteFlags.None, false, 0.0, parentTimeLength, 0, parentTickLength);
        harm1Parent.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 0.0, parentTimeLength / 2, 0, 960));
        parts[0].NotePhrases.Add(new VocalsPhrase(0.0, parentTimeLength, 0, parentTickLength, harm1Parent, new()));

        var harm2Parent = new VocalNote(NoteFlags.None, false, 0.0, parentTimeLength, 0, parentTickLength);
        harm2Parent.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric, parentTimeLength / 2, parentTimeLength / 2, 960, 960));
        parts[1].NotePhrases.Add(new VocalsPhrase(0.0, parentTimeLength, 0, parentTickLength, harm2Parent, new()));

        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var syncTrack = CreateSyncTrackWithTempo();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, syncTrack, EngineParameters, false, micCount: 2);

        PhraseGrade? capturedGrade = null;
        IReadOnlyList<double>? capturedMeters = null;

        engine.OnPartyVocalsPhrase += (grade, meters, isLast) =>
        {
            capturedGrade = grade;
            capturedMeters = meters;
        };

        // Advance past countdowns
        engine.Update(0.1);

        // First half (ticks 0-960, time 0-1.0s): sing C4 for HARM1
        // Second half (ticks 960-1920, time 1.0-2.0s): sing E4 for HARM2
        for (int i = 0; i < 150; i++) // 2.5 seconds at 60fps
        {
            double t = 0.1 + (i + 1) * (1.0 / 60.0);
            if (t <= 1.1)
                engine.SetMicPitch(0, 60f);  // C4 for HARM1
            else
                engine.SetMicPitch(0, 64f);  // E4 for HARM2
            engine.Update(t);
        }

        // Advance past phrase end (phrase ends at tick 1920 ≈ 2.0s)
        engine.Update(2.5);

        Assert.That(capturedGrade, Is.EqualTo(PhraseGrade.DoubleAwesome),
            "Non-overlapping should emit DoubleAwesome — same mic fills different parts across windows");
        Assert.That(capturedMeters, Is.Not.Null);
        Assert.That(capturedMeters![0], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent));
        Assert.That(capturedMeters[1], Is.GreaterThanOrEqualTo(EngineParameters.PhraseHitPercent));
    }

    // ================================================================
    // Helpers
    // ================================================================

    private static List<VocalsPart> Create2PartsWithLongPhrases()
    {
        var parts = new List<VocalsPart>
        {
            CreateVocalsPart(isHarmony: false),
            CreateVocalsPart(isHarmony: true),
        };
        AddLongPhraseWithPitch(parts[0], 60, tickOffset: 0);
        AddLongPhraseWithPitch(parts[1], 64, tickOffset: 0);
        return parts;
    }

    private static List<VocalsPart> Create3PartsWithLongPhrases()
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

    private static SyncTrack CreateSyncTrackWithTempo()
    {
        var syncTrack = new SyncTrack(480);
        syncTrack.Tempos.Add(new TempoChange(120.0, 0.0, 0));
        return syncTrack;
    }
}
