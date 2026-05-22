using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;

namespace YARG.Core.UnitTests.Engine;

/// <summary>
/// Scenario tests for Party Vocals engine behavior.
/// Each test constructs chart data, feeds per-mic pitch sequences to the engine,
/// and asserts phrase grades. The engine emits one OnPartyVocalsPhrase event per
/// phrase with a combined grade (Awesome, DoubleAwesome, TripleAwesome, or Miss).
/// </summary>
[TestFixture]
public sealed class PartyVocalsScenarioTests
{
    private static readonly VocalsEngineParameters EngineParams = new(
        new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0),
        4,
        VocalsEngineTests.StarMultiplierThresholds,
        VocalsEngineTests.SoloBonusStarMultiplierThresholds,
        1.5f, 0.5f, 0.75, 60.0, true, 1000);

    private const int ApproximateVocalFps = 60;

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

    private static (List<PhraseGrade> grades, int score) RunScenario(
        List<VocalsPart> parts, int micCount, Action<YargFreeVocalsEngine> feedAction, double endTime)
    {
        var primaryChart = parts[0].CloneAsInstrumentDifficulty();
        var engine = new YargFreeVocalsEngine(primaryChart, parts, CreateSyncTrack(), EngineParams, false, micCount: micCount);
        var grades = new List<PhraseGrade>();
        engine.OnPartyVocalsPhrase += (grade, meters, isLast) => grades.Add(grade);
        engine.Update(0.1);
        feedAction(engine);
        engine.Update(endTime);
        var stats = engine.BaseStats;
        return (grades, stats.CommittedScore);
    }

    private static void FeedPitches(YargFreeVocalsEngine engine, int micCount,
        float[][] micPitchArrays, double startTime, double duration)
    {
        if (micPitchArrays.Length < micCount)
            throw new ArgumentException($"Expected {micCount} pitch arrays, got {micPitchArrays.Length}");

        int totalFrames = (int)(duration * ApproximateVocalFps);
        for (int f = 0; f < totalFrames; f++)
        {
            double time = startTime + (f + 1) / ApproximateVocalFps;
            for (int m = 0; m < micCount; m++)
            {
                int idx = Math.Min(f, micPitchArrays[m].Length - 1);
                engine.SetMicPitch(m, micPitchArrays[m][idx]);
            }
            engine.Update(time);
        }
    }

    // ================================================================
    // Overlap validation (3 scenarios)
    // ================================================================

    [Test]
    public void TwoNonOverlapping_TwoMics_ScorePositive()
    {
        // Two parts at different times. Engine emits grade per primary chart phrase.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 960, 960, 64);

        var (grades, score) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { -1f } }, 0.1, 2.5);
            FeedPitches(engine, 2, new[] { new[] { 64f }, new[] { -1f } }, 2.6, 2.5);
        }, 6.0);

        Assert.Multiple(() =>
        {
            Assert.That(grades.Count, Is.GreaterThanOrEqualTo(1), "At least one phrase grade");
            Assert.That(score, Is.GreaterThan(0), "Score should be positive");
            Assert.That(grades[0], Is.EqualTo(PhraseGrade.Awesome), "First phrase should be Awesome");
        });
    }

    [Test]
    public void ThreeNonOverlapping_TwoMics_AtLeastOneAwesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 960, 960, 64);
        AddPhrase(parts[2], 1920, 960, 67);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { -1f } }, 0.1, 2.5);
            FeedPitches(engine, 2, new[] { new[] { 64f }, new[] { -1f } }, 2.6, 2.5);
            FeedPitches(engine, 2, new[] { new[] { 67f }, new[] { -1f } }, 5.1, 2.5);
        }, 9.0);

        Assert.Multiple(() =>
        {
            Assert.That(grades.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(grades[0], Is.EqualTo(PhraseGrade.Awesome));
        });
    }

    [Test]
    public void TwoOverlapOneFree_TwoMics_AwesomePerPhrase()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 480, 960, 64);
        AddPhrase(parts[2], 1920, 960, 67);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { 64f } }, 0.1, 4.0);
            FeedPitches(engine, 2, new[] { new[] { 67f }, new[] { -1f } }, 4.1, 2.5);
        }, 8.0);

        Assert.Multiple(() =>
        {
            Assert.That(grades.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(grades[0], Is.EqualTo(PhraseGrade.DoubleAwesome), "Overlapping HARM1+HARM2 both covered");
        });
    }

    // ================================================================
    // Basic scenarios (3 scenarios)
    // ================================================================

    [Test]
    public void SinglePartBasic_OneMic_Awesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);

        var (grades, score) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { -1f } }, 0.1, 2.5);
        }, 4.0);

        Assert.Multiple(() =>
        {
            Assert.That(grades.Count, Is.EqualTo(1));
            Assert.That(grades[0], Is.EqualTo(PhraseGrade.Awesome));
            Assert.That(score, Is.GreaterThan(0));
        });
    }

    [Test]
    public void TwoPartBasic_TwoMics_DoubleAwesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 0, 960, 64);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { 64f } }, 0.1, 2.5);
        }, 4.0);

        Assert.Multiple(() =>
        {
            Assert.That(grades.Count, Is.EqualTo(1), "One combined grade for overlapping phrases");
            Assert.That(grades[0], Is.EqualTo(PhraseGrade.DoubleAwesome));
        });
    }

    [Test]
    public void ThreePartBasic_ThreeMics_TripleAwesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 0, 960, 64);
        AddPhrase(parts[2], 0, 960, 67);

        var (grades, _) = RunScenario(parts, 3, engine =>
        {
            FeedPitches(engine, 3, new[] { new[] { 60f }, new[] { 64f }, new[] { 67f } }, 0.1, 2.5);
        }, 4.0);

        Assert.Multiple(() =>
        {
            Assert.That(grades.Count, Is.EqualTo(1), "One combined grade");
            Assert.That(grades[0], Is.EqualTo(PhraseGrade.TripleAwesome));
        });
    }

    // ================================================================
    // Discord community scenarios (8 scenarios)
    // ================================================================

    [Test]
    public void TwoPartUnisonDiverge_OneMic_AwesomeOnFirstPart()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 0, 480, 60);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { -1f } }, 0.1, 2.5);
        }, 4.0);

        Assert.That(grades.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.Awesome));
    }

    [Test]
    public void ThreePartBackupHarmony_TwoMics_DoubleAwesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 0, 960, 64);
        AddPhrase(parts[2], 0, 960, 67);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { 64f } }, 0.1, 2.5);
        }, 4.0);

        Assert.That(grades.Count, Is.EqualTo(1), "One combined grade for simultaneous phrases");
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.DoubleAwesome), "2 of 3 parts covered");
    }

    [Test]
    public void ThreePartStaggeredEntry_ThreeMics_AllAwesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 1440, 60);
        AddPhrase(parts[1], 480, 960, 64);
        AddPhrase(parts[2], 960, 480, 67);

        var (grades, _) = RunScenario(parts, 3, engine =>
        {
            FeedPitches(engine, 3, new[] { new[] { 60f }, new[] { 64f }, new[] { 67f } }, 0.1, 4.0);
        }, 6.0);

        Assert.That(grades.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.TripleAwesome), "All 3 parts covered by 3 mics");
    }

    [Test]
    public void PartSwitchingChallenge_TwoMics_DoubleAwesome()
    {
        // Two overlapping parts. Mics swap which part they sing mid-phrase.
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 0, 960, 64);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            // Mic 0 sings HARM1, mic 1 sings HARM2
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { 64f } }, 0.1, 2.5);
        }, 4.0);

        Assert.That(grades.Count, Is.EqualTo(1));
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.DoubleAwesome), "Both parts covered");
    }

    [Test]
    public void TalkiesOverlapChallenge_TwoMics_AwesomeBoth()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddTalkiePhrase(parts[1], 0, 960);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { 60f } }, 0.1, 2.5);
        }, 4.0);

        Assert.That(grades.Count, Is.EqualTo(1), "One combined grade");
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.DoubleAwesome), "Pitched + talkie = both parts hit");
    }

    [Test]
    public void UltimateTalkieChaos_ThreeMics_TripleAwesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true), CreateVocalsPart(true) };
        AddTalkiePhrase(parts[0], 0, 960);
        AddTalkiePhrase(parts[1], 0, 960);
        AddTalkiePhrase(parts[2], 0, 960);

        var (grades, _) = RunScenario(parts, 3, engine =>
        {
            FeedPitches(engine, 3, new[] { new[] { 60f }, new[] { 64f }, new[] { 67f } }, 0.1, 2.5);
        }, 4.0);

        Assert.That(grades.Count, Is.EqualTo(1), "One combined grade");
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.TripleAwesome), "All talkies hit");
    }

    [Test]
    public void ShepardToneEdgeCase_LowAndHighPitches_Awesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 36);
        AddTalkiePhrase(parts[1], 0, 960);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 36f }, new[] { -1f } }, 0.1, 2.5);
        }, 4.0);

        Assert.That(grades.Count, Is.EqualTo(1));
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.DoubleAwesome), "Low pitch + talkie = both parts hit");
    }

    [Test]
    public void ThreePartTalkies_TwoMics_DoubleAwesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true), CreateVocalsPart(true) };
        AddTalkiePhrase(parts[0], 0, 960);
        AddTalkiePhrase(parts[1], 0, 960);
        AddTalkiePhrase(parts[2], 0, 960);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { 64f } }, 0.1, 2.5);
        }, 4.0);

        Assert.That(grades.Count, Is.EqualTo(1), "One combined grade");
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.DoubleAwesome), "2 of 3 talkies covered by 2 mics");
    }

    // ================================================================
    // Talkie-specific scenarios (3 additional)
    // ================================================================

    [Test]
    public void TalkieOnly_NoPitchInput_StillAwesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddTalkiePhrase(parts[0], 0, 960);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            for (int i = 0; i < 90; i++)
            {
                engine.SetMicPitch(0, -1f);
                engine.SetMicPitch(1, -1f);
                engine.Update(0.1 + (i + 1) / 60.0);
            }
        }, 4.0);

        Assert.That(grades.Count, Is.EqualTo(1));
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.Awesome), "Talkie should hit without pitch");
    }

    [Test]
    public void MixedTalkieAndPitched_SameTime_OneMic_DoubleAwesome()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddTalkiePhrase(parts[1], 0, 960);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 60f }, new[] { -1f } }, 0.1, 2.5);
        }, 4.0);

        Assert.That(grades.Count, Is.EqualTo(1), "One combined grade");
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.DoubleAwesome), "Pitched + talkie = both hit");
    }

    [Test]
    public void AllMiss_WrongPitch_MissGrade()
    {
        var parts = new List<VocalsPart> { CreateVocalsPart(), CreateVocalsPart(true) };
        AddPhrase(parts[0], 0, 960, 60);
        AddPhrase(parts[1], 0, 960, 64);

        var (grades, _) = RunScenario(parts, 2, engine =>
        {
            FeedPitches(engine, 2, new[] { new[] { 90f }, new[] { 30f } }, 0.1, 2.5);
        }, 4.0);

        Assert.That(grades.Count, Is.EqualTo(1), "One combined grade");
        Assert.That(grades[0], Is.EqualTo(PhraseGrade.Miss), "Both parts missed with wrong pitch");
    }
}
