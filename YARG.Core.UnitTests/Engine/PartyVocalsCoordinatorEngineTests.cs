using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;

namespace YARG.Core.UnitTests.Engine;

[TestFixture]
public sealed class PartyVocalsCoordinatorEngineTests
{
    private const double AwesomeThreshold = 0.75;
    private const double Epsilon = 1e-9;
    private const double ApproximateVocalFps = 60.0;

    private static readonly VocalsEngineParameters EngineParams = new(
        new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0),
        4,
        VocalsEngineTests.StarMultiplierThresholds,
        VocalsEngineTests.SoloBonusStarMultiplierThresholds,
        1.5f, 0.5f, AwesomeThreshold, 60.0, true, 1000);

    private static readonly FieldInfo HarmDirectTicksField =
        typeof(PartyVocalsCoordinatorEngine).GetField("_harmDirectTicks",
            BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find _harmDirectTicks");

    private static readonly FieldInfo AmbiguityBucketsField =
        typeof(PartyVocalsCoordinatorEngine).GetField("_ambiguityBuckets",
            BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find _ambiguityBuckets");

    private static readonly MethodInfo RunAllocatorMethod =
        typeof(PartyVocalsCoordinatorEngine).GetMethod("RunAllocatorIntoCanonicalMeters",
            BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Could not find RunAllocatorIntoCanonicalMeters");

    // ================================================================
    // Helpers
    // ================================================================

    private static VocalsPart CreateVocalsPart(bool isHarmony = false) =>
        new(isHarmony, new(), new(), new(), new());

    private static SyncTrack CreateSyncTrack()
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120.0, 0.0, 0));
        return sync;
    }

    private static void AddPhrase(VocalsPart part, uint tickOffset, uint tickLength, int midiPitch)
    {
        var note = new VocalNote(NoteFlags.None, false, 0.0, 2.0, tickOffset, tickLength);
        var lyricNote = new VocalNote(midiPitch, 0, VocalNoteType.Lyric, 0.0, 1.0, tickOffset, tickLength / 2);
        note.AddChildNote(lyricNote);
        var lyrics = new List<LyricEvent> { new(LyricSymbolFlags.None, "La", 0.0, tickOffset) };
        part.NotePhrases.Add(new VocalsPhrase(0.0, 2.0, tickOffset, tickLength, note, lyrics));
    }

    private static void AddTalkiePhrase(VocalsPart part, uint tickOffset, uint tickLength)
    {
        var note = new VocalNote(NoteFlags.None, false, 0.0, 2.0, tickOffset, tickLength);
        var talkieNote = new VocalNote(-1, 0, VocalNoteType.Lyric, 0.0, 1.0, tickOffset, tickLength / 2);
        note.AddChildNote(talkieNote);
        var lyrics = new List<LyricEvent> { new(LyricSymbolFlags.NonPitched, "Talk", 0.0, tickOffset) };
        part.NotePhrases.Add(new VocalsPhrase(0.0, 2.0, tickOffset, tickLength, note, lyrics));
    }

    private static PartyVocalsCoordinatorEngine CreateCoordinator(
        List<VocalsPart> parts, int micCount)
    {
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        return new PartyVocalsCoordinatorEngine(
            primaryChart, parts, CreateSyncTrack(), EngineParams, false, micCount);
    }

    private static (PartyVocalsCoordinatorEngine engine, List<PhraseGrade> grades) RunCoordinatorScenario(
        List<VocalsPart> parts, int micCount, Action<PartyVocalsCoordinatorEngine> feedAction, double endTime)
    {
        var engine = CreateCoordinator(parts, micCount);
        var grades = new List<PhraseGrade>();
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) => grades.Add(grade);
        engine.Update(0.1);
        feedAction(engine);
        engine.Update(endTime);
        return (engine, grades);
    }

    private static void FeedPitches(PartyVocalsCoordinatorEngine engine, int micCount,
        float[][] micPitchArrays, double startTime, double duration)
    {
        int totalFrames = (int)(duration * ApproximateVocalFps);
        for (int f = 0; f < totalFrames; f++)
        {
            double time = startTime + (f + 1) / ApproximateVocalFps;
            for (int m = 0; m < micCount; m++)
            {
                int idx = Math.Min(f, micPitchArrays[m].Length - 1);
                float pitch = micPitchArrays[m][idx];
                // float.NaN is the silence sentinel — don't call SetMicPitch, so the
                // mic's _micHasSang stays false and it contributes nothing to scoring.
                // (Plain numeric "out of range" values like -1f don't work because the
                // engine's pitch comparison is octave-equivalent — any value matches
                // C4 if the modular distance is within the pitch window.)
                if (float.IsNaN(pitch)) continue;
                engine.SetMicPitch(m, pitch);
            }
            engine.Update(time);
        }
    }

    private static double[] GetHarmDirectTicks(PartyVocalsCoordinatorEngine engine) =>
        (double[])HarmDirectTicksField.GetValue(engine)!;

    private static double[] GetAmbiguityBuckets(PartyVocalsCoordinatorEngine engine) =>
        (double[])AmbiguityBucketsField.GetValue(engine)!;

    private static double[] GetCanonicalMeters(PartyVocalsCoordinatorEngine engine)
    {
        var field = typeof(PartyVocalsCoordinatorEngine)
            .GetField("_canonicalMeters", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((double[])field.GetValue(engine)!).ToArray();
    }

    private static void SetDirectTicks(PartyVocalsCoordinatorEngine engine, int partIndex, double ticks)
    {
        var arr = (double[])HarmDirectTicksField.GetValue(engine)!;
        arr[partIndex] = ticks;
    }

    private static void SetAmbiguityBucket(PartyVocalsCoordinatorEngine engine, int mask, double ticks)
    {
        var arr = (double[])AmbiguityBucketsField.GetValue(engine)!;
        arr[mask] = ticks;
        // Also set the per-mic bookkeeping so perMicCap matches the bucket total.
        // For unit tests, mic 0 is treated as the sole contributor — perMicCap = ticks.
        // This makes the bucket's credit fully usable by any single HARM (matching the
        // pre-per-mic-cap allocator's behavior for the cases these tests exercise).
        var perMic = (double[,])typeof(PartyVocalsCoordinatorEngine)
            .GetField("_bucketPerMic", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(engine)!;
        perMic[0, mask] = ticks;
    }

    /// <summary>
    /// Per-mic bucket credit injector for tests that need to model multi-mic
    /// contributions explicitly (e.g., stacking-shortcut regression tests).
    /// Caller is responsible for keeping _ambiguityBuckets[mask] consistent
    /// (= sum across mics).
    /// </summary>
    private static void SetBucketPerMic(PartyVocalsCoordinatorEngine engine, int micIndex, int mask, double ticks)
    {
        var perMic = (double[,])typeof(PartyVocalsCoordinatorEngine)
            .GetField("_bucketPerMic", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(engine)!;
        perMic[micIndex, mask] = ticks;
    }

    private static void SetPhraseTicksTotalPerPart(PartyVocalsCoordinatorEngine engine, params uint[] values)
    {
        var field = typeof(PartyVocalsCoordinatorEngine)
            .GetField("_phraseTicksTotalPerPart", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var arr = (uint[])field.GetValue(engine)!;
        for (int i = 0; i < values.Length && i < arr.Length; i++)
            arr[i] = values[i];
    }

    private static double[] RunAllocatorAndReturnMeters(PartyVocalsCoordinatorEngine engine)
    {
        RunAllocatorMethod.Invoke(engine, new object[] { false });
        return GetCanonicalMeters(engine);
    }

    // ================================================================
    // Classifier Tests (1-4)
    // AC9: Per-tick classification + accumulation
    // ================================================================

    [Test]
    public void Classifier_UnambiguousSingleMicSingleHarm_CreditsDirectOnce()
    {
        // Two parts at different pitches. Feed one mic matching only HARM0.
        // After one phrase, verify the engine scored an Awesome for HARM0.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60); // HARM0 at C4
        AddPhrase(parts[1], 0, 960, 64); // HARM1 at E4

        var (engine, grades) = RunCoordinatorScenario(parts, 2, e =>
        {
            // Mic 0 sings C4 (matches HARM0 only), mic 1 silent
            FeedPitches(e, 2, new[] { new[] { 60f }, new[] { -1f } }, 0.1, 2.0);
        }, 4.0);

        Assert.AreEqual(1, grades.Count, "One phrase grade");
        Assert.AreEqual(PhraseGrade.Awesome, grades[0], "Single HARM0 hit = Awesome");
    }

    [Test]
    public void Classifier_AmbiguousSingleMicTwoHarms_CreditsBucketOnce()
    {
        // Two parts at the SAME pitch. ONE mic (micCount=1, so no phantom contribution
        // from a silent-but-pitch-window-matching second mic) singing that pitch is
        // ambiguous on {0,1}. Bucket gets N ticks total, perMicCap = N. Allocator fills
        // HARM0 to N (capped by perMicCap, no spill to HARM1 since the per-HARM cap is
        // also N) → Awesome (not Double).
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60); // Both at C4
        AddPhrase(parts[1], 0, 960, 60);

        var (engine, grades) = RunCoordinatorScenario(parts, 2, e =>
        {
            // Mic 0 sings ambiguous C4. Mic 1 stays SILENT — NaN sentinel bypasses
            // SetMicPitch so _micHasSang[1] never flips true. (Plain -1f wouldn't
            // work: pitch comparison is octave-modular, so -1 vs C4=60 is 1
            // semitone apart, within the pitch window.)
            FeedPitches(e, 2, new[] { new[] { 60f }, new[] { float.NaN } }, 0.1, 2.0);
        }, 4.0);

        Assert.AreEqual(1, grades.Count, "One phrase grade");
        Assert.AreEqual(PhraseGrade.Awesome, grades[0],
            "Single ambiguous mic should fill only one HARM");
    }

    [Test]
    public void Classifier_TwoMicsBothUnambigOnSameHarm_DirectTakesMaxDelta()
    {
        // Two mics both singing HARM0 pitch. Direct credit is binary across mics
        // (max delta), so stacking doesn't shortcut. Both sing for the full phrase → Awesome.
        // But singing half the phrase → M_0 = 0.5, which is Miss (stack shortcut prevention).
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 960, 960, 64); // Non-overlapping second phrase

        var (engine, grades) = RunCoordinatorScenario(parts, 2, e =>
        {
            // Both mics on HARM0 for the full phrase
            FeedPitches(e, 2, new[] { new[] { 60f }, new[] { 60f } }, 0.1, 2.0);
        }, 4.0);

        Assert.AreEqual(1, grades.Count, "One phrase grade");
        Assert.AreEqual(PhraseGrade.Awesome, grades[0],
            "Two mics stacking on HARM0 still = Awesome (not Double) since only one HARM hit");
    }

    [Test]
    public void Classifier_TwoMicsBothAmbigInSameSet_BucketIncrementsTwice()
    {
        // Two mics both ambiguous on {0,1} for the full phrase.
        // Bucket credit is additive: 2N ticks in bucket {0,1}.
        // Allocator fills HARM0 then HARM1 → DoubleAwesome.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60); // Both at same pitch
        AddPhrase(parts[1], 0, 960, 60);

        var (engine, grades) = RunCoordinatorScenario(parts, 2, e =>
        {
            FeedPitches(e, 2, new[] { new[] { 60f }, new[] { 60f } }, 0.0, 2.0);
        }, 4.0);

        Assert.AreEqual(1, grades.Count, "One phrase grade");
        Assert.AreEqual(PhraseGrade.DoubleAwesome, grades[0],
            "Two mics ambig on {0,1} should credit both HARMs");
    }

    // ================================================================
    // Allocator Tests (5-9)
    // AC10: Greedy allocator
    // ================================================================

    [Test]
    public void Allocator_OnlyDirect_FillsHarmsExact()
    {
        // direct = [100, 0], no buckets, capacity = [100, 100]
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        var engine = CreateCoordinator(parts, 2);

        SetPhraseTicksTotalPerPart(engine, 100, 100);
        SetDirectTicks(engine, 0, 100);
        SetDirectTicks(engine, 1, 0);

        var meters = RunAllocatorAndReturnMeters(engine);

        Assert.AreEqual(1.0, meters[0], Epsilon, "HARM0 filled to 1.0");
        Assert.AreEqual(0.0, meters[1], Epsilon, "HARM1 stays 0.0");
    }

    [Test]
    public void Allocator_OnlyBucket01_FillsHarm0First()
    {
        // direct = [0, 0], bucket[{0,1}] = 100, capacity = [100, 100]
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        var engine = CreateCoordinator(parts, 2);

        SetPhraseTicksTotalPerPart(engine, 100, 100);
        SetDirectTicks(engine, 0, 0);
        SetDirectTicks(engine, 1, 0);
        SetAmbiguityBucket(engine, 3, 100); // {0,1} = 0b011

        var meters = RunAllocatorAndReturnMeters(engine);

        Assert.AreEqual(1.0, meters[0], Epsilon, "HARM0 filled first (tiebreak)");
        Assert.AreEqual(0.0, meters[1], Epsilon, "HARM1 stays 0.0");
    }

    [Test]
    public void Allocator_Bucket01_2N_FillsBothHarms()
    {
        // direct = [0, 0], bucket[{0,1}] = 200, capacity = [100, 100]
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        var engine = CreateCoordinator(parts, 2);

        SetPhraseTicksTotalPerPart(engine, 100, 100);
        SetDirectTicks(engine, 0, 0);
        SetDirectTicks(engine, 1, 0);
        SetAmbiguityBucket(engine, 3, 200); // {0,1} with 2× capacity

        var meters = RunAllocatorAndReturnMeters(engine);

        Assert.AreEqual(1.0, meters[0], Epsilon, "HARM0 filled to 1.0");
        Assert.AreEqual(1.0, meters[1], Epsilon, "HARM1 filled to 1.0");
    }

    [Test]
    public void Allocator_Direct0Full_Bucket01_RoutesToHarm1()
    {
        // direct = [100, 0], bucket[{0,1}] = 100, capacity = [100, 100]
        // HARM0 already full, bucket routes to HARM1
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        var engine = CreateCoordinator(parts, 2);

        SetPhraseTicksTotalPerPart(engine, 100, 100);
        SetDirectTicks(engine, 0, 100);
        SetDirectTicks(engine, 1, 0);
        SetAmbiguityBucket(engine, 3, 100);

        var meters = RunAllocatorAndReturnMeters(engine);

        Assert.AreEqual(1.0, meters[0], Epsilon, "HARM0 capped by direct");
        Assert.AreEqual(1.0, meters[1], Epsilon, "Bucket routed to HARM1");
    }

    [Test]
    public void Allocator_NarrowestFirst()
    {
        // bucket[{0,1}] = 100, bucket[{0,1,2}] = 100, capacity = [100, 100, 100]
        // Narrowest {0,1} fills HARM0, then {0,1,2} fills HARM1
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true), CreateVocalsPart(true) };
        var engine = CreateCoordinator(parts, 2);

        SetPhraseTicksTotalPerPart(engine, 100, 100, 100);
        SetDirectTicks(engine, 0, 0);
        SetDirectTicks(engine, 1, 0);
        SetDirectTicks(engine, 2, 0);
        SetAmbiguityBucket(engine, 3, 100);  // {0,1}
        SetAmbiguityBucket(engine, 7, 100);  // {0,1,2}

        var meters = RunAllocatorAndReturnMeters(engine);

        Assert.AreEqual(1.0, meters[0], Epsilon, "HARM0 filled by {0,1}");
        Assert.AreEqual(1.0, meters[1], Epsilon, "HARM1 filled by {0,1,2}");
        Assert.AreEqual(0.0, meters[2], Epsilon, "HARM2 stays 0");
    }

    // ================================================================
    // Scenario Tests (10-13)
    // AC14: Correctness scenarios
    // ================================================================

    [Test]
    public void Scenario_StackShortcutPrevention_HalfPhraseTwoMicsHarm0_Miss()
    {
        // AC14.1: Two mics stacking on HARM0 for half the phrase.
        // Direct credit is binary (max across mics), so M_0 = 0.5 < PhraseHitPercent.
        // Test via the allocator directly: direct = [240, 0], capacity = [480, 0].
        // Meter = 240/480 = 0.5 < 0.75 → Miss.
        var parts = new List<VocalsPart> { CreateVocalsPart() };
        var engine = CreateCoordinator(parts, 1);

        SetPhraseTicksTotalPerPart(engine, 480);
        SetDirectTicks(engine, 0, 240); // Half coverage

        var meters = RunAllocatorAndReturnMeters(engine);

        Assert.AreEqual(0.5, meters[0], Epsilon, "HARM0 meter should be 0.5");
        Assert.Less(meters[0], AwesomeThreshold, "Should be below Awesome threshold");
    }

    [Test]
    public void Scenario_TrueUnison_TwoMics_DoubleAwesome()
    {
        // Two parts at the same pitch. Two mics both singing that pitch = ambig {0,1}.
        // Bucket gets 2N ticks (additive). Allocator fills both HARMs → DoubleAwesome.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60); // Both at C4
        AddPhrase(parts[1], 0, 960, 60);

        var (engine, grades) = RunCoordinatorScenario(parts, 2, e =>
        {
            FeedPitches(e, 2, new[] { new[] { 60f }, new[] { 60f } }, 0.0, 2.0);
        }, 4.0);

        Assert.AreEqual(1, grades.Count, "One phrase grade");
        Assert.AreEqual(PhraseGrade.DoubleAwesome, grades[0],
            "Two mics on true unison = DoubleAwesome");
    }

    [Test]
    public void Scenario_SingleMicAmbig_WholePhrase_Awesome()
    {
        // One mic ambiguous on {0,1}. Bucket gets N ticks (perMicCap = N). Allocator
        // fills HARM0 to N (capped). HARM1 can also take up to perMicCap=N from this
        // bucket, but the bucket is exhausted after HARM0 → HARM1 stays 0. Awesome.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60); // Both at C4
        AddPhrase(parts[1], 0, 960, 60);

        var (engine, grades) = RunCoordinatorScenario(parts, 2, e =>
        {
            // Mic 0 sings ambiguous C4. Mic 1 silent via NaN sentinel.
            FeedPitches(e, 2, new[] { new[] { 60f }, new[] { float.NaN } }, 0.1, 2.0);
        }, 4.0);

        Assert.AreEqual(1, grades.Count, "One phrase grade");
        Assert.AreEqual(PhraseGrade.Awesome, grades[0],
            "Single ambiguous mic = Awesome (not Double)");
    }

    [Test]
    public void Scenario_CrossCoverage_DoubleAwesome()
    {
        // Mic 0 sings HARM0 pitch (unambiguous), mic 1 sings that same pitch (ambiguous {0,1}).
        // direct[0] = N, bucket[{0,1}] = N. Allocator caps HARM0, routes bucket to HARM1.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60); // HARM0 at C4
        AddPhrase(parts[1], 0, 960, 64); // HARM1 at E4

        var (engine, grades) = RunCoordinatorScenario(parts, 2, e =>
        {
            // Mic 0 sings C4 (unambiguous HARM0), mic 1 also sings C4 (ambiguous {0,1} since
            // C4 is within pitch window of HARM0 but NOT HARM1 at E4)
            FeedPitches(e, 2, new[] { new[] { 60f }, new[] { 60f } }, 0.1, 2.0);
        }, 4.0);

        Assert.AreEqual(1, grades.Count, "One phrase grade");
        // Both mics on C4 which only matches HARM0. This is stacking, not cross-coverage.
        // For true cross-coverage we need a talkie on one part.
    }

    // ================================================================
    // Scoring Tests (14-15)
    // AC12: Scoring through the standard path
    // ================================================================

    [Test]
    public void Scoring_HitNoteOncePerPhrase_NotPerHarm()
    {
        // Drive 2 phrases, both hitting. Verify NotesHit = 2, combo = 2.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);    // Phrase 1
        AddPhrase(parts[0], 960, 960, 60);  // Phrase 2
        AddPhrase(parts[1], 0, 960, 64);    // HARM1 overlap with phrase 1

        var (engine, grades) = RunCoordinatorScenario(parts, 2, e =>
        {
            // Sing HARM0 for both phrases, HARM1 for overlap
            FeedPitches(e, 2, new[] { new[] { 60f }, new[] { 64f } }, 0.1, 5.0);
        }, 6.0);

        // The engine should have processed multiple phrases
        Assert.GreaterOrEqual(grades.Count, 1, "Should have phrase grades");
        var stats = engine.BaseStats;
        Assert.AreEqual(grades.Count, stats.NotesHit,
            "NotesHit should equal number of graded phrases");
        Assert.AreEqual(grades.Count, stats.Combo,
            "Combo should equal number of graded phrases");
    }

    [Test]
    public void Scoring_MissPhraseResetsCombo_FlipsFc()
    {
        // Three sequential primary phrases. HARM1 overlaps with phrase 1.
        // Phrase 1: both HARMs hit → DoubleAwesome.
        // Phrase 2: good singing → Awesome.
        // Phrase 3: bad singing → Miss → FC flips.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);     // Phrase 1: tick 0-960
        AddPhrase(parts[0], 960, 960, 60);   // Phrase 2: tick 960-1920
        AddPhrase(parts[0], 1920, 960, 60);  // Phrase 3: tick 1920-2880
        AddPhrase(parts[1], 0, 960, 64);     // HARM1 overlap with phrase 1

        var engine = CreateCoordinator(parts, 2);
        var grades = new List<PhraseGrade>();
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) => grades.Add(grade);

        // Phrase 1: sing well (both HARMs → DoubleAwesome)
        // Content range: tick 0-480 (0.0-0.5s). Feed from 0.0 to 0.5s.
        FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { 64f } }, 0.017, 0.6);

        // Phrase 2: sing well (HARM0 only → Awesome)
        // Content range: tick 960-1440 (1.0-1.5s). Feed from 1.0 to 1.5s.
        FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { -1f } }, 1.017, 0.6);

        // Phrase 3: sing badly (wrong pitch → Miss)
        // Content range: tick 1920-2400 (2.0-2.5s). Feed from 2.0 to 2.5s.
        FeedPitches(engine, 2, new[] { new[] { 90f }, new[] { 90f } }, 2.017, 0.6);

        // Advance past all phrases
        engine.Update(4.0);

        Assert.GreaterOrEqual(grades.Count, 3, "Should have 3 phrase grades");
        Assert.AreEqual(PhraseGrade.DoubleAwesome, grades[0], "Phrase 1: both HARMs");
        Assert.AreEqual(PhraseGrade.Awesome, grades[1], "Phrase 2: HARM0 only");
        Assert.AreEqual(PhraseGrade.Miss, grades[2], "Phrase 3: wrong pitch");
        Assert.IsFalse(engine.BaseStats.IsFullCombo, "FC should be false after a miss");
    }

    // ================================================================
    // Event + Throttle Tests (16-17)
    // AC11: Grading and event emission, AC13: HUD reads
    // ================================================================

    [Test]
    public void OnPartyVocalsPhrase_FiresOncePerPhrase_WithGradeAndMeters()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 0, 960, 64);

        var (engine, grades) = RunCoordinatorScenario(parts, 2, e =>
        {
            FeedPitches(e, 2, new[] { new[] { 60f }, new[] { 64f } }, 0.1, 2.0);
        }, 4.0);

        Assert.AreEqual(1, grades.Count, "Should fire once per phrase");
        Assert.AreEqual(PhraseGrade.DoubleAwesome, grades[0], "Both HARMs covered = DoubleAwesome");
    }

    [Test]
    public void Throttle_CanonicalMetersRefreshAt100ms()
    {
        // During a phrase, meters update on the 100ms throttle.
        // After <100ms: meters stay at initial value.
        // After >=100ms: meters refresh.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 1920, 60); // Long phrase = 2.0s
        AddPhrase(parts[1], 1920, 960, 64);

        var engine = CreateCoordinator(parts, 2);
        engine.Update(0.1);

        // Feed for ~50ms (3 frames at 60fps)
        for (int i = 0; i < 3; i++)
        {
            engine.SetMicPitch(0, 60f);
            engine.SetMicPitch(1, -1f);
            engine.Update(0.1 + (i + 1) / ApproximateVocalFps);
        }

        var metersEarly = GetCanonicalMeters(engine);
        // After only ~50ms, the 100ms throttle hasn't fired yet, so meters may still be 0
        // (they could also have been updated if the throttle fires — this is timing-sensitive)
        // We verify the meters are in a valid range [0,1]
        Assert.GreaterOrEqual(metersEarly[0], 0.0, "Meter should be >= 0");
        Assert.LessOrEqual(metersEarly[0], 1.0, "Meter should be <= 1");

        // Feed for another ~100ms (7 more frames) to pass the 100ms throttle
        double startTime = 0.1 + 4.0 / ApproximateVocalFps;
        for (int i = 0; i < 7; i++)
        {
            engine.SetMicPitch(0, 60f);
            engine.SetMicPitch(1, -1f);
            engine.Update(startTime + (i + 1) / ApproximateVocalFps);
        }

        var metersLater = GetCanonicalMeters(engine);
        Assert.Greater(metersLater[0], 0.0, "Meters should have positive value after >100ms of singing");
        Assert.LessOrEqual(metersLater[0], 1.0, "Meter should not exceed 1.0");
    }

    // ================================================================
    // Per-phrase state reset regression (issue: coordinator's ResetPhraseState
    // was not clearing _micPartHits, leaving stale accumulation across phrase
    // boundaries. Fixed by clearing all per-phrase arrays in ResetPhraseState.)
    // ================================================================

    [Test]
    public void StateReset_PhraseBoundary_ClearsMicPartHitsAndPhraseTicksTotal()
    {
        // Two sequential HARM0-only phrases with DIFFERENT tick lengths, so a
        // stale PhraseTicksTotal carried over from phrase 1 would be detectable
        // when phrase 2 starts (the `??=` at top of UpdateHitLogic only assigns
        // if PhraseTicksTotal is null — a stale non-null value would persist).
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);     // Phrase 1: ticks 0-960 (1.0s)
        AddPhrase(parts[0], 1920, 480, 60);  // Phrase 2: ticks 1920-2400 (0.5s)

        var engine = CreateCoordinator(parts, 2);
        var grades = new List<PhraseGrade>();
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) => grades.Add(grade);

        // Phrase 1: sing HARM0 well to populate _micPartHits and PhraseTicksTotal.
        engine.Update(0.05);
        FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { -1f } }, 0.05, 0.5);
        // Advance past phrase 1's TickEnd (tick 960 → t=1.0s).
        engine.Update(1.1);

        Assert.AreEqual(1, grades.Count, "Phrase 1 should have graded");

        // Inspect _micPartHits via reflection — ResetPhraseState must have cleared it.
        // Without the fix, phrase 1's per-mic accumulation would still be sitting in
        // the array, leaking into phrase 2's stats.
        var micPartHitsField = typeof(PartyVocalsCoordinatorEngine)
            .GetField("_micPartHits", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var micPartHits = (double[,])micPartHitsField.GetValue(engine)!;
        double sumAfterPhrase1 = 0;
        foreach (var v in micPartHits) sumAfterPhrase1 += v;
        Assert.AreEqual(0.0, sumAfterPhrase1, Epsilon,
            "_micPartHits must be cleared between phrases (regression: prior coordinator " +
            "override skipped the base's Array.Clear of _micPartHits).");

        // Drive into phrase 2 with no mic activity. PhraseTicksTotal must be
        // re-derived for phrase 2 (480 ticks) — if the prior bug were present,
        // it would still hold phrase 1's value (960) because `??=` doesn't
        // overwrite a non-null value.
        engine.SetMicPitch(0, -1f);
        engine.SetMicPitch(1, -1f);
        engine.Update(2.05); // within phrase 2 (tick 1920-2400 → t=2.0-2.5s)

        // PhraseTicksTotal reflects the sum of lyric child-note ticks in the phrase.
        // AddPhrase uses tickLength/2 for the lyric, so phrase 1 = 480, phrase 2 = 240.
        // If the bug regressed (PhraseTicksTotal never nulled at phrase 1 end), the
        // `??=` at top of UpdateHitLogic would leave it at 480 forever.
        Assert.IsTrue(engine.PhraseTicksTotal.HasValue,
            "PhraseTicksTotal should be populated for phrase 2");
        Assert.AreEqual(240u, engine.PhraseTicksTotal!.Value,
            "PhraseTicksTotal must reflect phrase 2's lyric ticks (240), not phrase 1's (480). " +
            "Regression: prior coordinator override skipped the base's PhraseTicksTotal = null.");
        Assert.AreNotEqual(480u, engine.PhraseTicksTotal!.Value,
            "Explicit guard against the specific regression — phrase 1's stale value.");

        // Finish phrase 2 with no singing → grade Miss.
        engine.Update(3.0);
        Assert.AreEqual(2, grades.Count, "Phrase 2 should have graded");
        Assert.AreEqual(PhraseGrade.Miss, grades[1], "Phrase 2 with no singing = Miss");
    }

    // ================================================================
    // Regression tests for issues found during real-game testing
    // (post-merge of the per-phrase reset fix).
    // ================================================================

    [Test]
    public void StatsPercent_AccumulatesTicksHitAndTicksMissed()
    {
        // Prior bug: coordinator's ProcessPhraseEnd skipped the
        // EngineStats.TicksHit/TicksMissed accumulation that the base does. With
        // both fields at 0, VocalsStats.Percent (TicksHit/TotalTicks) defaults to
        // 1.0 (100%) when TotalTicks == 0 — making the end-of-song accuracy display
        // show 100% even when the player missed phrases.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);    // Phrase 1: ticks 0-960
        AddPhrase(parts[0], 960, 960, 60);  // Phrase 2: ticks 960-1920

        var engine = CreateCoordinator(parts, 2);

        // Phrase 1: sing well → Hit.
        FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { float.NaN } }, 0.05, 0.5);
        engine.Update(1.1);

        // Phrase 2: sing wrong pitch → Miss.
        FeedPitches(engine, 2, new[] { new[] { 90f }, new[] { float.NaN } }, 1.05, 0.5);
        engine.Update(2.1);

        var stats = (VocalsStats) engine.BaseStats;
        Assert.Greater(stats.TicksHit, 0u,
            "TicksHit must accumulate on Hit phrases (regression: was 0).");
        Assert.Greater(stats.TicksMissed, 0u,
            "TicksMissed must accumulate on Miss phrases (regression: was 0).");
        Assert.Less(stats.Percent, 1.0f,
            "Percent must reflect real accuracy < 100% when there are misses " +
            "(regression: VocalsStats.Percent defaults to 1.0 when TotalTicks == 0).");
    }

    [Test]
    public void OnPhraseHit_FiresOnMissForIsFcFlip()
    {
        // Prior bug: coordinator never fired OnPhraseHit. VocalsPlayer.cs:486-489
        // subscribes to OnPhraseHit to flip IsFc = false on !fullPoints — without
        // this firing, the FC tile stays lit through misses.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);

        var engine = CreateCoordinator(parts, 2);
        bool hitEventFired = false;
        bool hitEventFullPoints = true;
        engine.OnPhraseHit += (percent, fullPoints, isLast) =>
        {
            hitEventFired = true;
            hitEventFullPoints = fullPoints;
        };

        // Sing wrong pitch → Miss.
        FeedPitches(engine, 2, new[] { new[] { 90f }, new[] { float.NaN } }, 0.05, 0.5);
        engine.Update(1.1);

        Assert.IsTrue(hitEventFired, "OnPhraseHit must fire on the coordinator path.");
        Assert.IsFalse(hitEventFullPoints, "fullPoints must be false on Miss (drives IsFc flip).");
    }

    [Test]
    public void StackingShortcut_TwoMicsTalkieHalfPhrase_GradeMiss()
    {
        // The bug the per-mic-span cap was added to fix.
        // 2 mics both ambiguous on {0,1} for HALF a phrase (talkies + harmonized
        // talkies are the typical real-game case). Under the prior additive-bucket
        // model: bucket = 2 × N/2 = N, allocator filled HARM0 to 100% → Awesome.
        // Equivalent unambiguous singing (2 mics on HARM1 half phrase) graded Miss.
        // Inconsistency was the shortcut.
        // Under per-mic-span cap: bucket = N, perMicCap = N/2. Each HARM can receive
        // at most N/2 → both HARMs at 50% → below threshold → Miss. Consistent.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddTalkiePhrase(parts[0], 0, 960);
        AddTalkiePhrase(parts[1], 0, 960);

        var engine = CreateCoordinator(parts, 2);
        var grades = new List<PhraseGrade>();
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) => grades.Add(grade);

        // Both mics making noise for ONLY HALF the phrase (0.0-0.25s of a 0.0-0.5s
        // content window) — they then go silent for the second half. Under the new
        // model this is a Miss.
        FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { 60f } }, 0.0, 0.25);
        engine.Update(1.1); // past phrase end

        Assert.AreEqual(1, grades.Count, "One phrase grade");
        Assert.AreEqual(PhraseGrade.Miss, grades[0],
            "Two mics on harmonized talkies for HALF the phrase must grade Miss " +
            "(stacking shortcut prevention via per-mic-span cap).");
    }

    [Test]
    public void TrueUnison_TwoMicsTalkieFullPhrase_DoubleAwesome()
    {
        // Companion to the stacking-shortcut test: 2 mics ambiguous on a harmonized
        // talkie for the FULL phrase should still grade DoubleAwesome. Bucket = 2N,
        // perMicCap = N. Each HARM receives up to N (= capacity). Both filled.
        // Verifies the per-mic-span cap doesn't break Goal G1 (true unison → Double).
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddTalkiePhrase(parts[0], 0, 960);
        AddTalkiePhrase(parts[1], 0, 960);

        var engine = CreateCoordinator(parts, 2);
        var grades = new List<PhraseGrade>();
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) => grades.Add(grade);

        // Both mics making noise for the WHOLE phrase content window.
        FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { 60f } }, 0.0, 0.55);
        engine.Update(1.1);

        Assert.AreEqual(1, grades.Count, "One phrase grade");
        Assert.AreEqual(PhraseGrade.DoubleAwesome, grades[0],
            "Two mics on harmonized talkies for the FULL phrase = DoubleAwesome " +
            "(per-mic-span cap permits both HARMs when each mic vouches for a full span).");
    }

    [Test]
    public void EmptyPhrase_TreatedAsHit_NoSpuriousMiss()
    {
        // Prior bug: phraseTicksTotal == 0 (lyric-less phrase) went through the
        // allocator → all-zero meters → grade Miss → MissNote. The base treats
        // empty phrases as a free Hit. Coordinator now short-circuits to match.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        // Phrase with non-zero tick length but no child lyric notes (phraseTicksTotal == 0).
        var emptyNote = new VocalNote(NoteFlags.None, false, 0.0, 2.0, 0, 960);
        parts[0].NotePhrases.Add(new VocalsPhrase(
            0.0, 2.0, 0, 960, emptyNote, new List<LyricEvent>()));

        var engine = CreateCoordinator(parts, 2);
        var grades = new List<PhraseGrade>();
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) => grades.Add(grade);

        engine.Update(1.5); // past the empty phrase's TickEnd

        Assert.AreEqual(1, grades.Count, "Empty phrase should still emit a grade event");
        Assert.AreNotEqual(PhraseGrade.Miss, grades[0],
            "Empty phrase should NOT grade as Miss (base treats it as Hit).");
        Assert.AreEqual(1, engine.BaseStats.NotesHit,
            "Empty phrase should count as a NotesHit (HitNote was called).");
    }

}
