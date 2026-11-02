using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Vocals.Engines;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

[TestFixture]
public sealed class FreeHarmonyCreditAllocationTests
{
    private const double EPSILON = 1e-6;
    private const uint NOTE_TICKS = 1920;
    private const double FPS = 60;

    // Canonical meters are raw fractions. A visibly full meter is Awesome at
    // PhraseHitPercent, not at raw 1.0. Matching masks remain candidate sets.
    [TestCase(2, 0.75)]
    [TestCase(3, 0.75)]
    [TestCase(2, 0.40)]
    [TestCase(3, 0.25)]
    public void SimultaneousTalkies_OnlyOneMeterAdvancesUntilAwesome(int partCount, double threshold)
    {
        var engine = CreateEngine(threshold, Enumerable.Repeat((-1, 0u), partCount).ToArray());
        Feed(engine, 0, 0.2, 60f);
        var first = Snapshot(engine);

        Assert.Multiple(() =>
        {
            Assert.That(first.Count(value => value > EPSILON), Is.EqualTo(1),
                "One microphone must not advance several talkie meters simultaneously");
            Assert.That(first.Sum(), Is.InRange(0.07, 0.11),
                "About 0.2 seconds of a 2-second line is one microphone's credit budget");
            Assert.That(engine.GetMicHittingParts(0), Is.EqualTo((1u << partCount) - 1),
                "Unselected matching talkies must remain candidates");
        });

        int selected = Array.IndexOf(first, first.Max());
        Feed(engine, 0.2, 0.4, 60f);
        var second = Snapshot(engine);
        AssertOnlySelectedAdvances(first, second, selected);
    }

    [TestCase(2, 0.40, -1, PhraseGrade.DoubleAwesome)]
    [TestCase(3, 0.25, -1, PhraseGrade.TripleAwesome)]
    [TestCase(2, 0.40, 60, PhraseGrade.DoubleAwesome)]
    [TestCase(3, 0.25, 60, PhraseGrade.TripleAwesome)]
    public void MatchingParts_AfterVisibleAwesome_HandOffAndCanEarnSequentialAwards(
        int partCount, double threshold, int notePitch, PhraseGrade expectedGrade)
    {
        var engine = CreateEngine(threshold, Enumerable.Repeat((notePitch, 0u), partCount).ToArray());
        var grades = new List<PhraseGrade>();
        engine.OnPartyVocalsPhrase += (grade, parts, isLast) => grades.Add(grade);

        Feed(engine, 0, 0.2, 60f);
        var first = Snapshot(engine);
        int selected = Array.IndexOf(first, first.Max());
        Feed(engine, 0.2, 1.9, 60f);
        var later = Snapshot(engine);
        engine.Update(2.1);
        Assert.Multiple(() =>
        {
            Assert.That(later[selected], Is.GreaterThanOrEqualTo(threshold),
                "After all parts are Awesome, a matching full part may receive surplus credit");
            Assert.That(later.All(value => value >= threshold - EPSILON), Is.True,
                "Remaining singing time is sufficient to fill every part sequentially");
            Assert.That(later.Sum(), Is.LessThanOrEqualTo(0.96),
                "Sequential awards must still conserve the microphone's elapsed singing time");
            Assert.That(grades, Is.EqualTo(new[] { expectedGrade }));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public void OverlappingPitchedNotes_RetainEstablishedUnfinishedPart(int continuingPart)
    {
        // Establish one E4 before an equally matching E4 appears on the other part.
        var notes = new[] { (64, 288u), (64, 288u) };
        notes[continuingPart] = (64, 0u);
        var engine = CreateEngine(0.75, notes);
        Feed(engine, 0, 0.2, 64f);
        var before = Snapshot(engine);
        Feed(engine, 0.2, 0.6, 64f);
        var after = Snapshot(engine);
        AssertOnlySelectedAdvances(before, after, continuingPart);
        Assert.That(engine.GetMicHittingParts(0), Is.EqualTo(3u));
    }

    [TestCase(0, 64f)]
    [TestCase(1, 64f)]
    [TestCase(0, 65f)]
    [TestCase(1, 65f)]
    public void InitialSelection_PositivePitchMatchWinsOverTalkie(int pitchedPart, float pitch)
    {
        var notes = new[] { (-1, 0u), (-1, 0u) };
        notes[pitchedPart] = (64, 0u);
        var engine = CreateEngine(0.75, notes);
        // 65 is a partial positive pitch match, while the talkie matches at 100%.
        Feed(engine, 0, 0.4, pitch);
        var meters = Snapshot(engine);
        Assert.Multiple(() =>
        {
            Assert.That(meters[pitchedPart], Is.GreaterThan(0));
            Assert.That(meters[1 - pitchedPart], Is.EqualTo(0).Within(EPSILON),
                "A genuine pitched match takes initial credit even against a perfect talkie match");
            Assert.That(engine.GetMicHittingParts(0), Is.EqualTo(3u));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public void PitchedNoteAppears_DoesNotStealUnfinishedTalkie(int talkiePart)
    {
        var notes = new[] { (64, 288u), (64, 288u) };
        notes[talkiePart] = (-1, 0u);
        var engine = CreateEngine(0.75, notes);
        Feed(engine, 0, 0.2, 64f);
        var before = Snapshot(engine);
        Feed(engine, 0.2, 0.6, 64f);
        var after = Snapshot(engine);
        AssertOnlySelectedAdvances(before, after, talkiePart);
        Assert.That(engine.GetMicHittingParts(0), Is.EqualTo(3u));
    }

    [TestCase(0, 70f)]
    [TestCase(1, 70f)]
    [TestCase(0, 65.5f)]
    [TestCase(1, 65.5f)]
    public void NoPositivePitchCredit_StillCreditsTalkie(int talkiePart, float pitch)
    {
        var notes = new[] { (64, 0u), (64, 0u) };
        notes[talkiePart] = (-1, 0u);
        var engine = CreateEngine(0.75, notes);
        // 70 is off pitch; 65.5 is the zero-percent edge of the pitch window.
        // Both inputs represent vocal activity, not the test harness's NaN silence sentinel.
        Feed(engine, 0, 0.4, pitch);
        var meters = Snapshot(engine);
        Assert.Multiple(() =>
        {
            Assert.That(meters[talkiePart], Is.InRange(0.14, 0.21),
                "Allow the public meter's 100 ms refresh lag");
            Assert.That(meters[1 - talkiePart], Is.EqualTo(0).Within(EPSILON));
        });
    }

    [TestCase(2, 0.40)]
    [TestCase(3, 0.25)]
    public void MatchingParts_SequentialThresholdCheckpoints(int count, double threshold)
    {
        var engine = CreateEngine(threshold, Enumerable.Repeat((60, 0u), count).ToArray());
        Feed(engine, 0, threshold * 2 + 0.12, 60);
        var first = Snapshot(engine);
        Assert.That(first[0], Is.InRange(threshold, threshold + 0.01));
        Assert.That(first.Skip(1).Count(x => x > 0), Is.EqualTo(1));
        Feed(engine, threshold * 2 + 0.12, 1.9, 60);
        Assert.That(Snapshot(engine).All(x => x >= threshold), Is.True);
    }

    [TestCase(2)]
    [TestCase(3)]
    public void MatchingParts_HighThreshold_OnlySingleAwesome(int count)
    {
        var engine = CreateEngine(0.75, Enumerable.Repeat((60, 0u), count).ToArray());
        PhraseGrade? result = null;
        engine.OnPartyVocalsPhrase += (grade, _, _) => result = grade;
        Feed(engine, 0, 1.95, 60);
        Assert.That(Snapshot(engine).Count(x => x >= 0.75), Is.EqualTo(1));
        engine.Update(2.1);
        Assert.That(result, Is.EqualTo(PhraseGrade.Awesome));
    }

    [Test]
    public void MatchingParts_FinalEventPreservesCommittedCredit()
    {
        var engine = CreateEngine(0.4, (60, 0u), (60, 0u));
        double[] final = Array.Empty<double>();
        engine.OnPartyVocalsPhrase += (_, parts, _) => final = parts.Select(p => p.Meter).ToArray();
        Feed(engine, 0, 1.9, 60);
        engine.Update(2.0);
        var before = Snapshot(engine);
        engine.Update(2.1);
        Assert.That(final, Is.EqualTo(before).Within(EPSILON));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void InitialSelection_PartialPitchUsesOwnWeight(int part)
    {
        var notes = new[] { (-1, 0u), (-1, 0u) };
        notes[part] = (64, 0u);
        var engine = CreateEngine(0.75, notes);
        Feed(engine, 0, 0.4, 65);
        Assert.That(Snapshot(engine)[part], Is.InRange(0.07, 0.105));
        Assert.That(Snapshot(engine)[1 - part], Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void EstablishedPitch_LosesMatch_ReleasesToTalkie(int part)
    {
        var notes = new[] { (-1, 0u), (-1, 0u) };
        notes[part] = (64, 0u);
        var engine = CreateEngine(0.75, notes);
        Feed(engine, 0, 0.2, 64);
        var before = Snapshot(engine);
        Feed(engine, 0.2, 0.6, 70);
        AssertOnlySelectedAdvances(before, Snapshot(engine), 1 - part);
    }

    [Test]
    public void Silence_ClearsSelection_ReacquiresValidPart()
    {
        var engine = CreateEngine(0.75, (-1, 0u), (64, 288u));
        Feed(engine, 0, 0.2, 64);
        Feed(engine, 0.2, 0.4, float.NaN);
        var before = Snapshot(engine);
        Feed(engine, 0.4, 0.7, 64);
        AssertOnlySelectedAdvances(before, Snapshot(engine), 1);
    }

    [TestCase(2)]
    [TestCase(3)]
    public void MultipleMics_UnisonCreditsDistinctParts(int count)
    {
        var engine = CreateEngine(0.75, count, Enumerable.Repeat((60, 0u), count).ToArray());
        QueueSamples(engine, count, 0.4, false, false);
        Assert.That(Snapshot(engine).All(x => x >= 0.14 && x <= 0.21), Is.True);
    }

    [Test]
    public void Allocation_SameTimestampQueueOrderInvariant()
    {
        var forward = CreateEngine(0.4, 2, (60, 0u), (60, 0u));
        var reverse = CreateEngine(0.4, 2, (60, 0u), (60, 0u));
        QueueSamples(forward, 2, 0.8, false, false);
        QueueSamples(reverse, 2, 0.8, true, false);
        Assert.That(Snapshot(reverse), Is.EqualTo(Snapshot(forward)).Within(EPSILON));
        Assert.That(Snapshot(forward).All(x => x > 0.3), Is.True);
    }

    [Test]
    public void Allocation_FrameCadencePreservesTimestampedInputs()
    {
        var fine = CreateEngine(0.4, (60, 0u), (60, 0u));
        var coarse = CreateEngine(0.4, (60, 0u), (60, 0u));
        QueueSamples(fine, 1, 1.8, false, false);
        QueueSamples(coarse, 1, 1.8, false, true);
        fine.Update(1.9);
        coarse.Update(1.9);
        Assert.That(Snapshot(coarse), Is.EqualTo(Snapshot(fine)).Within(EPSILON));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(120)]
    public void Allocation_InputSampleRateBoundsOvershoot(int rate)
    {
        var engine = CreateEngine(0.4, (60, 0u), (60, 0u));
        for (int i = 1; i <= rate; i++)
        {
            var input = new GameInput(i / (double)rate, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 60f);
            engine.QueueInput(ref input);
            engine.Update(input.Time);
        }
        engine.Update(1.1);
        var meters = Snapshot(engine);
        Assert.That(meters.Sum(), Is.LessThanOrEqualTo(0.51));
        Assert.That(meters[0], Is.LessThanOrEqualTo(0.4 + 1.0 / 120 + EPSILON));
    }

    [Test]
    public void Allocation_PhraseResetAndSeekClearSelection()
    {
        var engine = CreateEngine(0.75, (-1, 0u), (64, 288u));
        Feed(engine, 0, 0.4, 64);
        engine.Reset();
        Feed(engine, 0, 0.4, 64);
        var fresh = CreateEngine(0.75, (-1, 0u), (64, 288u));
        Feed(fresh, 0, 0.4, 64);
        engine.Update(0.5);
        fresh.Update(0.5);
        Assert.That(Snapshot(engine), Is.EqualTo(Snapshot(fresh)).Within(EPSILON));
    }

    [Test]
    public void CandidateSpans_ClipAtNoteAndPhraseBoundaries()
    {
        var engine = CreateEngine(0.75, (60, 287u));
        Feed(engine, 0, 0.3, 60);
        var candidates = engine.SubEngines[0].CreditCandidates;
        Assert.That(candidates, Is.Not.Empty);
        foreach (var candidate in candidates)
        {
            Assert.That(candidate.Start, Is.GreaterThanOrEqualTo(287));
            Assert.That(candidate.End, Is.LessThanOrEqualTo(NOTE_TICKS));
            Assert.That(candidate.End, Is.GreaterThan(candidate.Start));
        }
    }

    [Test]
    public void FullMeters_MatchingAndDisplayTargetRemainLive()
    {
        var engine = CreateEngine(0.25, (60, 0u), (60, 0u));
        int targets = 0;
        engine.OnTargetNoteChanged += _ => targets++;
        Feed(engine, 0, 1.2, 60);
        int before = targets;
        Feed(engine, 1.2, 1.8, 60);
        Assert.That(Snapshot(engine).All(x => x >= 0.25 && x <= 1), Is.True);
        Assert.That(engine.GetMicHittingParts(0), Is.EqualTo(3));
        Assert.That(targets, Is.GreaterThan(before));
    }

    [Test]
    public void Allocation_LatestSameMicTimestampIsAuthoritative()
    {
        var engine = CreateEngine(0.75, (60, 0u), (64, 0u));
        var first = new GameInput(0.1, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 60f);
        var last = new GameInput(0.1, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 64f);
        engine.QueueInput(ref first);
        engine.QueueInput(ref last);
        engine.Update(0.1);
        engine.Update(0.2);
        Assert.That(Snapshot(engine)[0], Is.Zero);
        Assert.That(Snapshot(engine)[1], Is.GreaterThan(0));
    }

    [Test]
    public void Allocation_NoSampleFrameRetainsContinuity()
    {
        var engine = CreateEngine(0.75, (-1, 0u), (64, 208u));
        Feed(engine, 0, 0.2, 64);
        engine.Update(0.21);
        var next = new GameInput(0.225, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 64f);
        engine.QueueInput(ref next);
        engine.Update(0.225);
        engine.Update(0.325);
        Assert.That(Snapshot(engine)[1], Is.Zero);
        Assert.That(Snapshot(engine)[0], Is.GreaterThan(0.1));
    }

    [Test]
    public void MultipleMics_NewSelectionDoesNotStealUnsampledContinuity()
    {
        var engine = CreateEngine(0.75, 2, (60, 0u), (60, 0u));
        Feed(engine, 0, 0.2, 60);
        var next = new GameInput(0.21, PartyVocalsInput.Pack(1, VocalsAction.Pitch), 60f);
        engine.QueueInput(ref next);
        engine.Update(0.21);
        engine.Update(0.31);
        Assert.That(Snapshot(engine)[1], Is.GreaterThan(0));
    }

    [Test]
    public void SelectedCoverage_WeightedOverlapUsesMaximum()
    {
        var engine = CreateEngine(0.75, 2, (64, 0u));
        for (int frame = 1; frame <= 24; frame++)
        {
            double time = frame / FPS;
            var partial = new GameInput(time, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 65f);
            var perfect = new GameInput(time, PartyVocalsInput.Pack(1, VocalsAction.Pitch), 64f);
            engine.QueueInput(ref partial);
            engine.QueueInput(ref perfect);
            engine.Update(time);
        }
        engine.Update(0.5);
        Assert.That(Snapshot(engine)[0], Is.EqualTo(0.2).Within(0.002));
    }

    [Test]
    public void SelectedCoverage_DisjointIntervalsAdd()
    {
        var engine = CreateEngine(0.75, 2, (60, 0u));
        for (int frame = 1; frame <= 48; frame++)
        {
            double time = frame / FPS;
            int mic = frame <= 24 ? 0 : 1;
            var input = new GameInput(time, PartyVocalsInput.Pack(mic, VocalsAction.Pitch), 60f);
            engine.QueueInput(ref input);
            engine.Update(time);
        }
        engine.Update(0.9);
        Assert.That(Snapshot(engine)[0], Is.EqualTo(0.4).Within(0.003));
    }

    [Test]
    public void AdjacentNotes_ReleaseScoringContinuity()
    {
        var parts = new List<VocalsPart>();
        for (int partIndex = 0; partIndex < 2; partIndex++)
        {
            var part = new VocalsPart(true, new(), new(), new(), new(), new());
            var parent = new VocalNote(NoteFlags.None, false, 0, 2, 0, NOTE_TICKS);
            if (partIndex == 0)
                parent.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric, 0.3, 1.7, 288, 1632));
            else
            {
                parent.AddChildNote(new VocalNote(-1, 0, VocalNoteType.Lyric, 0, 0.3, 0, 288));
                parent.AddChildNote(new VocalNote(-1, 0, VocalNoteType.Lyric, 0.3, 1.7, 288, 1632));
            }
            part.NotePhrases.Add(new VocalsPhrase(0, 2, 0, NOTE_TICKS, parent, new()));
            parts.Add(part);
        }
        var engine = CreateEngine(0.75, 1, parts);
        Feed(engine, 0, 0.2, 64);
        var before = Snapshot(engine);
        Feed(engine, 0.2, 0.6, 64);
        var after = Snapshot(engine);
        Assert.That(after[0], Is.GreaterThan(before[0]));
        Assert.That(after[1], Is.LessThanOrEqualTo(0.151));
    }

    [TestCase(2)]
    [TestCase(3)]
    public void CarriedNotes_CreditEachOverlappingPhrase(int phraseCount)
    {
        var parts = CreatePhrases(phraseCount);
        uint end = (uint)(phraseCount * 960 - 240);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(
            new VocalNote(60, 0, VocalNoteType.Lyric, 0.5, (end - 480) / 960.0, 480, end - 480));
        var engine = CreateEngine(0.75, 1, parts);
        var results = new List<double>();
        engine.OnPartyVocalsPhrase += (_, values, _) => results.Add(values.Single().Meter);
        Feed(engine, 0, phraseCount, 60);
        engine.Update(phraseCount + 0.1);
        Assert.That(results.Count, Is.EqualTo(phraseCount));
        Assert.That(results, Is.All.GreaterThan(0.95));
    }

    [Test]
    public void AdjacentPhrases_BoundarySampleHasCadenceIndependentCredit()
    {
        double[][] Run(bool emptyBoundaryFrame)
        {
            var parts = CreatePhrases(2);
            foreach (var phrase in parts[0].NotePhrases)
                phrase.PhraseParentNote.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric,
                    phrase.Tick / 960.0, 1, phrase.Tick, 960));
            var engine = CreateEngine(0.75, 1, parts);
            var events = new List<double[]>();
            engine.OnPartyVocalsPhrase += (_, values, _) => events.Add(values.Select(x => x.Meter).ToArray());
            void Sample(uint tick)
            {
                var input = new GameInput(tick / 960.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 60f);
                engine.QueueInput(ref input);
                engine.Update(input.Time);
            }
            Sample(952);
            if (emptyBoundaryFrame) engine.Update(961 / 960.0);
            Sample(976);
            engine.Update(2.1);
            return events.ToArray();
        }
        var direct = Run(false);
        var separated = Run(true);
        Assert.That(direct.Length, Is.EqualTo(2));
        Assert.That(direct[0][0], Is.EqualTo(16 / 960.0).Within(EPSILON));
        Assert.That(direct[1][0], Is.EqualTo(16 / 960.0).Within(EPSILON));
        Assert.That(separated, Is.EqualTo(direct).Within(EPSILON));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SelectedPart_OverlappingNotesIntegrateMaximum(bool established)
    {
        var parts = CreatePhrases(1);
        var parent = parts[0].NotePhrases[0].PhraseParentNote;
        parent.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric, 0, 1, 0, 960));
        parent.AddChildNote(new VocalNote(65, 0, VocalNoteType.Lyric, 112 / 960.0, 848 / 960.0, 112, 848));
        double meter = 0;
        var measured = CreateEngine(0.75, 1, parts);
        measured.OnPartyVocalsPhrase += (_, values, _) => meter = values.Single().Meter;
        if (established)
        {
            var prior = new GameInput(100 / 960.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 65f);
            measured.QueueInput(ref prior);
            measured.Update(prior.Time);
        }
        var input = new GameInput(116 / 960.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 65f);
        measured.QueueInput(ref input);
        measured.Update(input.Time);
        measured.Update(1.1);
        Assert.That(meter * 960, Is.EqualTo(established ? 18 : 10).Within(EPSILON));
    }

    [Test]
    public void FullMeters_ValidTalkieContinuitySurvivesPitchedOnset()
    {
        var parts = CreatePhrases(1, 2);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric, 0, 0.2, 0, 192));
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric, 0.6, 0.4, 576, 384));
        parts[1].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(-1, 0, VocalNoteType.Lyric, 0, 1, 0, 960));
        var engine = CreateEngine(0.1, 1, parts);
        Feed(engine, 0, 0.55, 64);
        engine.Update(0.56);
        var before = Snapshot(engine);
        Feed(engine, 0.55, 0.8, 64);
        engine.Update(0.9);
        Assert.That(before.All(x => x >= 0.1), Is.True);
        AssertOnlySelectedAdvances(before, Snapshot(engine), 1);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OverlappingNotes_PerfectCoverageUsesUnionDenominator(bool carried)
    {
        var parts = CreatePhrases(carried ? 2 : 1);
        var first = parts[0].NotePhrases[0].PhraseParentNote;
        first.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 0, carried ? 1.5 : 1, 0, carried ? 1440u : 960u));
        var target = parts[0].NotePhrases[carried ? 1 : 0].PhraseParentNote;
        uint start = carried ? 960u : 112u;
        target.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, start / 960.0,
            (target.TickEnd - start) / 960.0, start, target.TickEnd - start));
        var engine = CreateEngine(0.75, 1, parts);
        var events = new List<double>();
        engine.OnPartyVocalsPhrase += (grade, values, _) =>
        {
            Assert.That(grade, Is.EqualTo(PhraseGrade.Awesome));
            events.Add(values.Single().Meter);
        };
        Feed(engine, 0, carried ? 2 : 1, 60);
        engine.Update(carried ? 2.1 : 1.1);
        Assert.That(events, Has.Count.EqualTo(carried ? 2 : 1));
        Assert.That(events, Is.All.EqualTo(1).Within(EPSILON));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void BoundaryStraddlingSample_CommitsExactPrefixAndSuffix(bool render, bool carried)
    {
        var parts = CreatePhrases(2);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(
            new VocalNote(60, 0, VocalNoteType.Lyric, 0, carried ? 2 : 968 / 960.0, 0, carried ? 1920u : 968u));
        if (!carried)
            parts[0].NotePhrases[1].PhraseParentNote.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 1, 1, 960, 960));
        var engine = CreateEngine(0.75, 1, parts);
        var meters = new List<double>();
        engine.OnPartyVocalsPhrase += (_, values, _) => meters.Add(values.Single().Meter);
        if (render) engine.Update(961 / 960.0);
        var input = new GameInput(968 / 960.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 60f);
        engine.QueueInput(ref input);
        engine.Update(input.Time);
        engine.Update(2.1);
        Assert.That(meters, Has.Count.EqualTo(2));
        Assert.That(meters[0], Is.EqualTo(8 / 960.0).Within(EPSILON));
        Assert.That(meters[1], Is.EqualTo(8 / 960.0).Within(EPSILON));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FinalPhrase_EndedSampleCreditsClippedPositivePrefix(bool render)
    {
        var parts = CreatePhrases(1);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 0, 1, 0, 960));
        var engine = CreateEngine(0.75, 1, parts);
        var meters = new List<double>();
        engine.OnPartyVocalsPhrase += (_, values, _) => meters.Add(values.Single().Meter);
        if (render) engine.Update(961 / 960.0);
        var input = new GameInput(968 / 960.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 60f);
        engine.QueueInput(ref input);
        engine.Update(input.Time);
        engine.Update(2);
        Assert.That(meters, Is.EqualTo(new[] { 8 / 960.0 }).Within(EPSILON));
        Assert.That(engine.CanonicalMeters.Single(), Is.Zero);
    }

    [Test]
    public void PercussionBoundary_PackedHitUsesCurrentPhrase(
        [Values(false, true)] bool render, [Values(false, true)] bool singToActivate,
        [Values(false, true)] bool ready, [Values(968u, 976u)] uint tick)
    {
        var parts = CreatePhrases(2);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 0, 1, 0, 960));
        var percussion = new VocalNote(-1, 0, VocalNoteType.Percussion, 968 / 960.0, 8 / 960.0, 968, 8);
        parts[0].NotePhrases[1].PhraseParentNote.AddChildNote(percussion);
        var engine = CreateEngine(0.75, 2, parts, FPS, false, singToActivate);
        if (ready) engine.EngineStats.StarPowerTickAmount = engine.TicksPerHalfSpBar + 100;
        var indices = new List<int>();
        engine.OnNoteHit += (index, note) => { if (note.IsPercussion) indices.Add(index); };
        int phraseEvents = 0;
        engine.OnPartyVocalsPhrase += (_, _, _) => phraseEvents++;
        if (render) engine.Update(961 / 960.0);
        double before = engine.BaseStats.TotalScore;
        var input = new GameInput(tick / 960.0, PartyVocalsInput.Pack(1, VocalsAction.Hit), true);
        engine.QueueInput(ref input);
        engine.Update(input.Time);
        Assert.That(indices, Is.EqualTo(new[] { 1 }));
        Assert.That(engine.BaseStats.TotalScore, Is.GreaterThan(before));
        double scored = engine.BaseStats.TotalScore;
        engine.Update(input.Time);
        Assert.That(engine.BaseStats.TotalScore, Is.EqualTo(scored));
        Assert.That(indices.Count, Is.EqualTo(1));
        Assert.That(engine.BaseStats.StarPowerActivationCount, Is.Zero);
        Assert.That(phraseEvents, Is.EqualTo(tick == 968 ? 0 : 1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PercussionBoundary_BotUsesCurrentPhrase(bool render)
    {
        var parts = CreatePhrases(2);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 0, 1, 0, 960));
        parts[0].NotePhrases[1].PhraseParentNote.AddChildNote(
            new VocalNote(-1, 0, VocalNoteType.Percussion, 968 / 960.0, 8 / 960.0, 968, 8));
        var engine = CreateEngine(0.75, 1, parts, FPS, true);
        var indices = new List<int>();
        engine.OnNoteHit += (index, note) => { if (note.IsPercussion) indices.Add(index); };
        if (render) engine.Update(961 / 960.0);
        engine.Update(968 / 960.0);
        Assert.That(indices, Is.EqualTo(new[] { 1 }));
        double score = engine.BaseStats.TotalScore;
        engine.Update(968 / 960.0);
        Assert.That(engine.BaseStats.TotalScore, Is.EqualTo(score));
        Assert.That(indices.Count, Is.EqualTo(1));
    }

    [Test]
    public void PhraseDisplay_BoundaryUsesSongTimeRatherThanPendingEvent()
    {
        var parts = CreatePhrases(3, 2);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 0, 1, 0, 960));
        parts[1].NotePhrases[1].PhraseParentNote.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric, 1, 1, 960, 960));
        var engine = CreateEngine(0.75, 1, parts);
        engine.Update(968 / 960.0);
        Assert.That(engine.PartInCurrentPhrase(0), Is.False);
        Assert.That(engine.PartInCurrentPhrase(1), Is.True);
        Assert.That(engine.PartInNextPhrase(1), Is.False);
        Assert.That(engine.CurrentPhraseProgress, Is.EqualTo(8 / 960.0).Within(EPSILON));
        Assert.That(engine.CurrentPhraseDurationSeconds, Is.EqualTo(1));
    }

    [TestCase(false, 2, 0, false, false)]
    [TestCase(true, 2, 1, false, false)]
    [TestCase(false, 3, 1, true, false)]
    [TestCase(true, 3, 0, true, false)]
    [TestCase(false, 2, 1, false, true)]
    [TestCase(true, 3, 0, true, true)]
    public void AsyncBoundary_IrrelevantMicDoesNotFinalizeOtherMicPrefix(
        bool final, int micCount, int scoredMic, bool nonmatch, bool nearAwesome)
    {
        (double[][] meters, PhraseGrade[] grades) Run(bool irrelevant)
        {
            var parts = CreatePhrases(final ? 1 : 2);
            parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(
                new VocalNote(60, 0, VocalNoteType.Lyric, 0, final ? 1 : 2, 0, final ? 960u : 1920u));
            var engine = CreateEngine(nearAwesome ? 0.02 : 0.75, micCount, parts);
            var meters = new List<double[]>();
            var grades = new List<PhraseGrade>();
            engine.OnPartyVocalsPhrase += (grade, values, _) =>
            {
                meters.Add(values.Select(x => x.Meter).ToArray());
                grades.Add(grade);
            };
            void Sample(uint tick, int mic, float pitch)
            {
                var input = new GameInput(tick / 960.0, PartyVocalsInput.Pack(mic, VocalsAction.Pitch), pitch);
                engine.QueueInput(ref input);
                engine.Update(input.Time);
            }
            if (nearAwesome) Sample(940, scoredMic, 60);
            if (irrelevant) Sample(961, 1 - scoredMic, nonmatch ? 66 : float.NaN);
            else engine.Update(961 / 960.0);
            Assert.That(meters, Is.Empty, "Immutable event must wait for every possible capped prefix");
            Sample(968, scoredMic, 60);
            Assert.That(meters, Is.Empty, "A different microphone may still submit a valid old prefix");
            engine.Update(2.1);
            Assert.That(meters[0][0], Is.EqualTo((nearAwesome ? 24 : 8) / 960.0).Within(EPSILON));
            if (!final) Assert.That(meters[1][0], Is.EqualTo(8 / 960.0).Within(EPSILON));
            return (meters.ToArray(), grades.ToArray());
        }
        var baseline = Run(false);
        var actual = Run(true);
        Assert.That(actual.meters, Is.EqualTo(baseline.meters).Within(EPSILON));
        Assert.That(actual.grades, Is.EqualTo(baseline.grades));
    }

    [Test]
    public void AsyncBoundary_ResetDropsAllPendingPhraseAllocations()
    {
        var parts = CreatePhrases(2);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 0, 2, 0, 1920));
        var engine = CreateEngine(0.75, 2, parts);
        var input = new GameInput(968 / 960.0, PartyVocalsInput.Pack(1, VocalsAction.Pitch), 60f);
        engine.QueueInput(ref input);
        engine.Update(input.Time);
        engine.Reset();
        var meters = new List<double>();
        engine.OnPartyVocalsPhrase += (_, values, _) => meters.Add(values.Single().Meter);
        engine.Update(2.1);
        Assert.That(meters, Is.EqualTo(new[] { 0d, 0d }));
    }

    [Test]
    public void AsyncBoundary_NextPhraseSuffixEstablishesContinuityImmediately()
    {
        var parts = CreatePhrases(2, 2);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(-1, 0, VocalNoteType.Lyric, 0, 2, 0, 1920));
        parts[1].NotePhrases[1].PhraseParentNote.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric,
            972 / 960.0, 948 / 960.0, 972, 948));
        var engine = CreateEngine(0.75, 2, parts);
        var events = new List<double[]>();
        engine.OnPartyVocalsPhrase += (_, values, _) => events.Add(values.Select(x => x.Meter).ToArray());
        foreach (uint tick in new[] { 968u, 976u })
        {
            var input = new GameInput(tick / 960.0, PartyVocalsInput.Pack(1, VocalsAction.Pitch), 64f);
            engine.QueueInput(ref input);
            engine.Update(input.Time);
            Assert.That(events.Count, Is.EqualTo(tick == 968 ? 0 : 1));
        }
        engine.Update(2.1);
        Assert.That(events[0], Is.EqualTo(new[] { 8 / 960.0 }).Within(EPSILON));
        Assert.That(events[1], Is.EqualTo(new[] { 16 / 960.0, 0d }).Within(EPSILON),
            "The suffix at968 establishes the talkie before pitched onset at972; work cannot be deferred to976");
    }

    [Test]
    public void BoundarySplit_NextPhraseMakesFreshRecipientSelection()
    {
        var parts = CreatePhrases(2, 2);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(new VocalNote(-1, 0, VocalNoteType.Lyric, 0, 2, 0, 1920));
        parts[1].NotePhrases[1].PhraseParentNote.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric, 1, 1, 960, 960));
        var engine = CreateEngine(0.75, 1, parts);
        var events = new List<double[]>();
        engine.OnPartyVocalsPhrase += (_, values, _) => events.Add(values.Select(x => x.Meter).ToArray());
        foreach (uint tick in new[] { 952u, 968u })
        {
            var input = new GameInput(tick / 960.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 64f);
            engine.QueueInput(ref input);
            engine.Update(input.Time);
        }
        engine.Update(2.1);
        Assert.That(events[0], Is.EqualTo(new[] { 24 / 960.0 }).Within(EPSILON));
        Assert.That(events[1], Is.EqualTo(new[] { 0d, 8 / 960.0 }).Within(EPSILON));
    }

    [Test]
    public void CoarseGap_OneCappedSampleSplitsAcrossSequentialBoundaries()
    {
        var part = new VocalsPart(true, new(), new(), new(), new(), new());
        foreach (var span in new[] { (0u, 100u), (100u, 108u), (108u, 200u) })
        {
            var parent = new VocalNote(NoteFlags.None, false, span.Item1 / 960.0,
                (span.Item2 - span.Item1) / 960.0, span.Item1, span.Item2 - span.Item1);
            if (span.Item1 == 0)
                parent.AddChildNote(new VocalNote(60, 0, VocalNoteType.Lyric, 0, 200 / 960.0, 0, 200));
            part.NotePhrases.Add(new VocalsPhrase(parent.Time, parent.TimeLength, parent.Tick, parent.TickLength, parent, new()));
        }
        var engine = CreateEngine(0.75, 1, new List<VocalsPart> { part });
        var meters = new List<double>();
        engine.OnPartyVocalsPhrase += (_, values, _) => meters.Add(values.Single().Meter);
        var input = new GameInput(112 / 960.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 60f);
        engine.QueueInput(ref input);
        engine.Update(input.Time);
        engine.Update(1);
        Assert.That(meters, Is.EqualTo(new[] { 4 / 100.0, 1.0, 4 / 92.0 }).Within(EPSILON));
    }

    [TestCase(2)]
    [TestCase(3)]
    public void Bot_CarriedNotesScoreAcrossPhrases(int count)
    {
        var parts = CreatePhrases(count);
        parts[0].NotePhrases[0].PhraseParentNote.AddChildNote(
            new VocalNote(60, 0, VocalNoteType.Lyric, 0, count, 0, (uint)count * 960));
        var engine = CreateEngine(0.75, 1, parts, FPS, true);
        var meters = new List<double>();
        engine.OnPartyVocalsPhrase += (_, values, _) => meters.Add(values.Single().Meter);
        for (int frame = 1; frame <= (count + 1) * FPS; frame++) engine.Update(frame / FPS);
        Assert.That(meters, Has.Count.EqualTo(count));
        Assert.That(meters, Is.All.GreaterThan(0.95));
    }

    [Test]
    public void ChangingWeights_LongPhrase_EmptyRenderUpdatesPreserveIntegral()
    {
        double Run(bool render)
        {
            var part = new VocalsPart(true, new(), new(), new(), new(), new());
            var parent = new VocalNote(NoteFlags.None, false, 0, 10, 0, 9600);
            parent.AddChildNote(new VocalNote(64, 0, VocalNoteType.Lyric, 0, 10, 0, 9600));
            part.NotePhrases.Add(new VocalsPhrase(0, 10, 0, 9600, parent, new()));
            var engine = CreateEngine(0.75, 3, new List<VocalsPart> { part }, 120);
            double meter = 0;
            engine.OnPartyVocalsPhrase += (_, values, _) => meter = values.Single().Meter;
            for (int frame = 1; frame <= 1200; frame++)
            {
                double time = frame / 120.0;
                for (int mic = 0; mic < 3; mic++)
                {
                    float pitch = frame % 2 == 0 ? 65f : 64.75f;
                    var input = new GameInput(time, PartyVocalsInput.Pack(mic, VocalsAction.Pitch), pitch);
                    engine.QueueInput(ref input);
                }
                engine.Update(time);
                if (render && frame < 1200) engine.Update(time + 1 / 480.0);
            }
            engine.Update(10.1);
            return meter;
        }
        double baseline = Run(false);
        Assert.That(baseline, Is.EqualTo(0.625).Within(0.001));
        Assert.That(Run(true), Is.EqualTo(baseline).Within(EPSILON));
    }

    [TestCase(0d)]
    [TestCase(-60d)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void Coordinator_InvalidSampleRateIsRejected(double fps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateEngine(0.75, 1, CreatePhrases(1), fps));
    }

    private static List<VocalsPart> CreatePhrases(int phraseCount, int partCount = 1)
    {
        var parts = new List<VocalsPart>();
        for (int partIndex = 0; partIndex < partCount; partIndex++)
        {
            var part = new VocalsPart(true, new(), new(), new(), new(), new());
            for (uint index = 0; index < phraseCount; index++)
            {
                uint start = index * 960;
                var parent = new VocalNote(NoteFlags.None, false, index, 1, start, 960);
                part.NotePhrases.Add(new VocalsPhrase(index, 1, start, 960, parent, new()));
            }
            parts.Add(part);
        }
        return parts;
    }

    private static void QueueSamples(PartyVocalsCoordinatorEngine engine, int mics, double end, bool reverse, bool coarse)
    {
        for (int frame = 1; frame <= (int)Math.Round(end * FPS); frame++)
        {
            double time = frame / FPS;
            for (int index = 0; index < mics; index++)
            {
                int mic = reverse ? mics - index - 1 : index;
                var input = new GameInput(time, PartyVocalsInput.Pack(mic, VocalsAction.Pitch), 60f);
                engine.QueueInput(ref input);
            }
            if (!coarse) engine.Update(time);
        }
        if (coarse) engine.Update(end);
    }

    private static void AssertOnlySelectedAdvances(double[] before, double[] after, int selected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(after[selected], Is.GreaterThan(before[selected]), "The continuing part must earn credit");
            for (int part = 0; part < after.Length; part++)
            {
                if (part != selected)
                {
                    Assert.That(after[part], Is.EqualTo(before[part]).Within(EPSILON),
                        $"Part {part} must not receive simultaneous or retrospectively reassigned credit");
                }
            }
        });
    }

    private static double[] Snapshot(PartyVocalsCoordinatorEngine engine) => engine.CanonicalMeters.ToArray();

    private static PartyVocalsCoordinatorEngine CreateEngine(double threshold, params (int pitch, uint start)[] notes) =>
        CreateEngine(threshold, 1, notes);

    private static PartyVocalsCoordinatorEngine CreateEngine(double threshold, int mics, params (int pitch, uint start)[] notes)
    {
        var parts = new List<VocalsPart>();
        foreach (var (pitch, start) in notes)
        {
            var part = new VocalsPart(true, new(), new(), new(), new(), new());
            var parent = new VocalNote(NoteFlags.None, false, 0, 2, 0, NOTE_TICKS);
            parent.AddChildNote(new VocalNote(pitch, 0, VocalNoteType.Lyric,
                start / 960.0, (NOTE_TICKS - start) / 960.0, start, NOTE_TICKS - start));
            var lyrics = new List<LyricEvent>
            {
                new(pitch < 0 ? LyricSymbolFlags.NonPitched : LyricSymbolFlags.None, "La", start / 960.0, start)
            };
            part.NotePhrases.Add(new VocalsPhrase(0, 2, 0, NOTE_TICKS, parent, lyrics));
            parts.Add(part);
        }

        return CreateEngine(threshold, mics, parts);
    }

    private static PartyVocalsCoordinatorEngine CreateEngine(double threshold, int mics, List<VocalsPart> parts, double fps = FPS, bool isBot = false, bool singToActivate = false)
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        sync.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 0, 0));
        var parameters = new VocalsEngineParameters(
            new HitWindowSettings(0.1, 0.1, 1, false, 0, 1, 1, 0, 0), 4,
            VocalsEngineTests.StarMultiplierThresholds, VocalsEngineTests.SoloBonusStarMultiplierThresholds,
            1.5f, 0.5f, threshold, fps, singToActivate, 1000);
        return new PartyVocalsCoordinatorEngine(parts[0].CloneAsInstrumentDifficulty(),
            parts, sync, parameters, isBot, mics);
    }

    private static void Feed(PartyVocalsCoordinatorEngine engine, double start, double end, float pitch)
    {
        int frames = (int)Math.Round((end - start) * FPS);
        for (int frame = 1; frame <= frames; frame++)
        {
            double time = start + frame / FPS;
            var input = new GameInput(time, PartyVocalsInput.Pack(0, VocalsAction.Pitch), pitch);
            engine.QueueInput(ref input);
            engine.Update(time);
        }
    }
}
