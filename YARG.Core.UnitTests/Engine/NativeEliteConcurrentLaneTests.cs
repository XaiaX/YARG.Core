using System;
using System.Collections.Generic;
using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.Parsing;
using static YARG.Core.Chart.EliteDrumNote;
using static YARG.Core.UnitTests.Parsing.MoonSongLoaderTests;

namespace YARG.Core.UnitTests.Engine;

/// <summary>Native authored pad lanes are independent even when chorded and interleaved with ordinary kicks.</summary>
public sealed class NativeEliteConcurrentLaneTests
{
    private static EliteDrumsEngine Create(InstrumentDifficulty<EliteDrumNote> difficulty)
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.ProFourLane);
        return new EliteDrumsEngine(difficulty, sync, parameters, false, true);
    }

    private static void Play(EliteDrumsEngine engine, params (double time, EliteDrumsAction action)[] strikes)
    {
        foreach (var (time, action) in strikes.OrderBy(strike => strike.time))
        {
            var input = GameInput.Create(time, action, 0.8f);
            engine.QueueInput(ref input);
        }
        engine.Update(6);
    }

    private static string Outcomes(InstrumentDifficulty<EliteDrumNote> chart) =>
        string.Join(", ", AllMembers(chart).Where(note => !note.WasHit)
            .Select(note => $"{(EliteDrumPad) note.Pad}@{note.Time:F3}:miss={note.WasMissed}"));

    private static IEnumerable<EliteDrumNote> AllMembers(InstrumentDifficulty<EliteDrumNote> chart) =>
        chart.Notes.SelectMany(parent => parent.ChildNotes.Prepend(parent));

    private static IEnumerable<EliteDrumNote> Members(InstrumentDifficulty<EliteDrumNote> chart,
        EliteDrumPad pad) => AllMembers(chart)
        .Where(note => note.Pad == (int) pad && !note.IsInvisibleTerminator);

    private static InstrumentDifficulty<EliteDrumNote> Load(Action<MoonChart> author,
        Difficulty difficulty = Difficulty.Expert)
    {
        var song = CreateSong();
        var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
        author(chart);
        return new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums)
            .GetDifficulty(difficulty);
    }

    private static MoonPhrase.Type Lane(EliteDrumPad pad) => pad switch
    {
        EliteDrumPad.LeftCrash => MoonPhrase.Type.EliteDrums_LeftCrashLane,
        EliteDrumPad.RightCrash => MoonPhrase.Type.EliteDrums_RightCrashLane,
        EliteDrumPad.HiHat => MoonPhrase.Type.EliteDrums_HiHatLane,
        EliteDrumPad.Tom1 => MoonPhrase.Type.EliteDrums_Tom1Lane,
        EliteDrumPad.Tom2 => MoonPhrase.Type.EliteDrums_Tom2Lane,
        EliteDrumPad.Kick => MoonPhrase.Type.EliteDrums_KickLane,
        _ => throw new ArgumentOutOfRangeException(nameof(pad))
    };

    private static void AuthorLane(MoonChart chart, EliteDrumPad pad, params double[] beats)
    {
        chart.Insert(new MoonPhrase(TICKS(beats[0]), TICKS(beats[^1] - beats[0] + 0.5), Lane(pad)));
        foreach (var beat in beats) chart.Insert(new MoonNote(TICKS(beat), (int) pad));
    }

    private static double[] ThirtySecondBeats(double start, double end) =>
        Enumerable.Range(0, (int) Math.Round((end - start) * 8) + 1)
            .Select(index => start + index / 8.0).ToArray();

    private static double[] SixteenthBeats(double start, double end) =>
        Enumerable.Range(0, (int) Math.Round((end - start) * 4) + 1)
            .Select(index => start + index / 4.0).ToArray();

    [TestCase(0.0, 8.0, TestName = "BothCrashes_TwoMeasuresAligned_WithNonLaneKicks")]
    [TestCase(0.125, 8.125, TestName = "BothCrashes_TwoMeasuresOffsetByThirtySecond_WithNonLaneKicks")]
    [TestCase(1.0, 7.0, TestName = "BothCrashes_NestedAcrossMeasures_WithNonLaneKicks")]
    public void TwoCrashLanesAndKeepingTimeKicks(double rightStart, double rightEnd)
    {
        var leftBeats = ThirtySecondBeats(0, 8);
        var rightBeats = ThirtySecondBeats(rightStart, rightEnd);
        var chart = Load(moon =>
        {
            AuthorLane(moon, EliteDrumPad.LeftCrash, leftBeats);
            AuthorLane(moon, EliteDrumPad.RightCrash, rightBeats);
            for (int i = 0; i <= 32; i++)
                moon.Insert(new MoonNote(TICKS(i * 0.25), (int) EliteDrumPad.Kick));
        });
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Select(record => record.AuthoredPad),
            Is.EquivalentTo(new[] { EliteDrumPad.LeftCrash, EliteDrumPad.RightCrash }));
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Select(record => record.MemberSources.Count),
            Is.EquivalentTo(new[] { leftBeats.Length, rightBeats.Length }));
        var kicks = Members(chart, EliteDrumPad.Kick).ToArray();
        Assert.That(kicks, Has.Length.EqualTo(33));
        var engine = Create(chart);
        Play(engine, Members(chart, EliteDrumPad.LeftCrash)
            .Select(note => (note.Time, EliteDrumsAction.EliteLeftCrash))
            .Concat(Members(chart, EliteDrumPad.RightCrash)
                .Select(note => (note.Time, EliteDrumsAction.EliteRightCrash)))
            .Concat(kicks.Select(kick => (kick.Time, EliteDrumsAction.Kick))).ToArray());
        Assert.Multiple(() =>
        {
            Assert.That(Members(chart, EliteDrumPad.LeftCrash).All(note => note.WasHit), Is.True);
            Assert.That(Members(chart, EliteDrumPad.RightCrash).All(note => note.WasHit), Is.True);
            Assert.That(kicks.All(note => note.WasHit), Is.True);
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(AllMembers(chart).Count()), Outcomes(chart));
        });
    }

    [Test]
    public void BothCrashLanesContinueIndependentlyAlongsideKeepingTimeKicks()
    {
        var chart = Load(moon =>
        {
            AuthorLane(moon, EliteDrumPad.LeftCrash, ThirtySecondBeats(0, 8));
            AuthorLane(moon, EliteDrumPad.RightCrash, ThirtySecondBeats(0, 8));
            for (int i = 0; i <= 32; i++)
                moon.Insert(new MoonNote(TICKS(i * 0.25), (int) EliteDrumPad.Kick));
        });
        var left = Members(chart, EliteDrumPad.LeftCrash).ToArray();
        var right = Members(chart, EliteDrumPad.RightCrash).ToArray();
        var kicks = Members(chart, EliteDrumPad.Kick).ToArray();
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Select(record => record.AuthoredPad),
            Is.EquivalentTo(new[] { EliteDrumPad.LeftCrash, EliteDrumPad.RightCrash }));
        var engine = Create(chart);
        Play(engine, new[]
        {
            (left[0].Time, EliteDrumsAction.EliteLeftCrash),
            (right[0].Time, EliteDrumsAction.EliteRightCrash)
        }.Concat(kicks.Select(kick => (kick.Time, EliteDrumsAction.Kick))).ToArray());
        Assert.Multiple(() =>
        {
            Assert.That(left.Take(2).All(note => note.WasHit), Is.True, Outcomes(chart));
            Assert.That(right.Take(2).All(note => note.WasHit), Is.True, Outcomes(chart));
            Assert.That(left.Skip(2).All(note => note.WasMissed), Is.True,
                "The opening crash must not carry a two-measure lane without real refresh.");
            Assert.That(right.Skip(2).All(note => note.WasMissed), Is.True,
                "Keeping-time kicks must not refresh either crash lane.");
            Assert.That(kicks.All(note => note.WasHit), Is.True, Outcomes(chart));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4 + kicks.Length));
        });
    }

    [TestCase(0.0, TestName = "TwoMeasureCrashLanes_Aligned_StopAfterPhysicalRefreshEnds")]
    [TestCase(0.125, TestName = "TwoMeasureCrashLanes_ThirtySecondOffset_StopAfterPhysicalRefreshEnds")]
    public void TwoMeasureCrashLanesRequireContinuedPhysicalRefresh(double rightOffset)
    {
        var leftBeats = ThirtySecondBeats(0, 8);
        var rightBeats = ThirtySecondBeats(rightOffset, 8 + rightOffset);
        var chart = Load(moon =>
        {
            AuthorLane(moon, EliteDrumPad.LeftCrash, leftBeats);
            AuthorLane(moon, EliteDrumPad.RightCrash, rightBeats);
            for (int i = 0; i <= 32; i++)
                moon.Insert(new MoonNote(TICKS(i * 0.25), (int) EliteDrumPad.Kick));
        });
        var left = Members(chart, EliteDrumPad.LeftCrash).ToArray();
        var right = Members(chart, EliteDrumPad.RightCrash).ToArray();
        var kicks = Members(chart, EliteDrumPad.Kick).ToArray();
        Assert.That(left, Has.Length.EqualTo(65));
        Assert.That(right, Has.Length.EqualTo(65));
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Select(record => record.MemberSources.Count),
            Is.EquivalentTo(new[] { 65, 65 }));
        // Refresh each lane physically throughout the first measure; only the
        // immediate next member may continue after those real strikes stop.
        var strikes = left.Take(33).Select(note => (note.Time, EliteDrumsAction.EliteLeftCrash))
            .Concat(right.Take(33).Select(note => (note.Time, EliteDrumsAction.EliteRightCrash)))
            .Concat(kicks.Select(note => (note.Time, EliteDrumsAction.Kick))).ToArray();
        var engine = Create(chart);
        Play(engine, strikes);
        Assert.Multiple(() =>
        {
            Assert.That(left.Take(34).All(note => note.WasHit), Is.True, Outcomes(chart));
            Assert.That(right.Take(34).All(note => note.WasHit), Is.True, Outcomes(chart));
            Assert.That(left.Skip(34).All(note => note.WasMissed), Is.True, Outcomes(chart));
            Assert.That(right.Skip(34).All(note => note.WasMissed), Is.True, Outcomes(chart));
            Assert.That(kicks.All(note => note.WasHit), Is.True, Outcomes(chart));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(34 * 2 + kicks.Length));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        });
    }

    [Test]
    public void HiHatDoubleTempoWhileCrashesAlternateAndOrdinaryKicksKeepTime()
    {
        var chart = Load(moon =>
        {
            AuthorLane(moon, EliteDrumPad.HiHat, ThirtySecondBeats(0, 8));
            AuthorLane(moon, EliteDrumPad.LeftCrash, SixteenthBeats(0, 8));
            AuthorLane(moon, EliteDrumPad.RightCrash, SixteenthBeats(0.125, 7.875));
            for (int i = 0; i <= 32; i++)
                moon.Insert(new MoonNote(TICKS(i * 0.25), (int) EliteDrumPad.Kick));
        });
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Select(record =>
                (record.AuthoredPad, record.MemberSources.Count)),
            Is.EquivalentTo(new[] { (EliteDrumPad.HiHat, 65), (EliteDrumPad.LeftCrash, 33),
                (EliteDrumPad.RightCrash, 32) }));
        var engine = Create(chart);
        Play(engine, Members(chart, EliteDrumPad.HiHat)
            .Select(note => (note.Time, EliteDrumsAction.EliteSizzleHiHat))
            .Concat(Members(chart, EliteDrumPad.LeftCrash)
                .Select(note => (note.Time, EliteDrumsAction.EliteLeftCrash)))
            .Concat(Members(chart, EliteDrumPad.RightCrash)
                .Select(note => (note.Time, EliteDrumsAction.EliteRightCrash)))
            .Concat(Members(chart, EliteDrumPad.Kick)
                .Select(kick => (kick.Time, EliteDrumsAction.Kick))).ToArray());
        Assert.Multiple(() =>
        {
            foreach (var pad in new[] { EliteDrumPad.HiHat, EliteDrumPad.LeftCrash,
                EliteDrumPad.RightCrash, EliteDrumPad.Kick })
                Assert.That(Members(chart, pad).All(note => note.WasHit), Is.True, pad.ToString());
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        });
    }

    [TestCase(false, TestName = "TwoMeasureHatAndCrashes_StopAllThreeLanesBreaksCombo")]
    [TestCase(true, TestName = "TwoMeasureTomsAndKick_StopAllThreeLanesBreaksCombo")]
    public void ThreeAuthoredLanesStopPlayingAndBreakCombo(bool tomsAndKick)
    {
        var chart = Load(moon =>
        {
            if (tomsAndKick)
            {
                AuthorLane(moon, EliteDrumPad.Tom1, SixteenthBeats(0, 8));
                AuthorLane(moon, EliteDrumPad.Tom2, SixteenthBeats(0, 8));
                AuthorLane(moon, EliteDrumPad.Kick, SixteenthBeats(0, 8));
            }
            else
            {
                AuthorLane(moon, EliteDrumPad.HiHat, ThirtySecondBeats(0, 8));
                AuthorLane(moon, EliteDrumPad.LeftCrash, SixteenthBeats(0, 8));
                AuthorLane(moon, EliteDrumPad.RightCrash, SixteenthBeats(0.125, 7.875));
            }
        });
        var pads = tomsAndKick
            ? new[] { EliteDrumPad.Tom1, EliteDrumPad.Tom2, EliteDrumPad.Kick }
            : new[] { EliteDrumPad.HiHat, EliteDrumPad.LeftCrash, EliteDrumPad.RightCrash };
        var actions = tomsAndKick
            ? new[] { EliteDrumsAction.EliteTom1, EliteDrumsAction.EliteTom2, EliteDrumsAction.Kick }
            : new[] { EliteDrumsAction.EliteSizzleHiHat, EliteDrumsAction.EliteLeftCrash,
                EliteDrumsAction.EliteRightCrash };
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Select(record => record.AuthoredPad),
            Is.EquivalentTo(pads));
        var strikes = pads.SelectMany((pad, index) => Members(chart, pad)
            .Where(note => note.Time < 2)
            .Select(note => (note.Time, actions[index]))).OrderBy(strike => strike.Time).ToArray();
        var engine = Create(chart);
        foreach (var (time, action) in strikes)
        {
            var input = GameInput.Create(time, action, 0.8f);
            engine.QueueInput(ref input);
        }
        engine.Update(1.99);
        Assert.That(engine.EngineStats.Combo, Is.GreaterThan(0), "Physical hits build combo before silence.");
        engine.Update(6);
        Assert.Multiple(() =>
        {
            foreach (var pad in pads)
            {
                var notes = Members(chart, pad).ToArray();
                Assert.That(notes.Where(note => note.Time < 2).All(note => note.WasHit),
                    Is.True, $"Opening measure {pad} must play physically.");
                Assert.That(notes.Where(note => note.Time >= 2.25).All(note => note.WasMissed),
                    Is.True, $"{pad} must expire rather than self-refresh throughout measure two.");
            }
            Assert.That(engine.EngineStats.Combo, Is.Zero, "Missed lane members break the live combo.");
            Assert.That(engine.EngineStats.MaxCombo, Is.GreaterThan(0));
            Assert.That(engine.EngineStats.NotesHit, Is.LessThan(engine.EngineStats.TotalNotes));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        });
    }

    [TestCase(Difficulty.Expert, false)]
    [TestCase(Difficulty.ExpertPlus, false)]
    [TestCase(Difficulty.ExpertPlus, true)]
    public void AuthoredKickLaneFiltersTwoXAndResolvesPhysicalKick(Difficulty difficulty, bool strikeTwoX)
    {
        var chart = Load(moon =>
        {
            moon.Add(new MoonPhrase(0, TICKS(2), Lane(EliteDrumPad.Kick)));
            for (int i = 0; i < 5; i++)
            {
                var kick = new MoonNote(TICKS(i * 0.2), (int) EliteDrumPad.Kick);
                if (i % 2 == 1) kick.flags |= MoonNote.Flags.InstrumentPlus;
                moon.Add(kick);
            }
        }, difficulty);
        var kicks = Members(chart, EliteDrumPad.Kick).ToArray();
        Assert.That(kicks.Length, Is.EqualTo(difficulty == Difficulty.Expert ? 3 : 5));
        Assert.That(kicks.Count(note => note.IsDoubleKick), Is.EqualTo(difficulty == Difficulty.Expert ? 0 : 2));
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Single().MemberSources.Count, Is.EqualTo(kicks.Length));
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Single().AuthoredPad, Is.EqualTo(EliteDrumPad.Kick));
        var engine = Create(chart);
        Play(engine, kicks.Where(note => note.IsDoubleKick == strikeTwoX)
            .Select(kick => (kick.Time, EliteDrumsAction.Kick)).ToArray());
        Assert.Multiple(() =>
        {
            Assert.That(kicks.Where(note => note.IsDoubleKick == strikeTwoX).All(note => note.WasHit), Is.True);
            Assert.That(engine.EngineStats.TotalNotes, Is.EqualTo(kicks.Length));
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(
                kicks.Count(note => note.IsDoubleKick == strikeTwoX)));
            Assert.That(kicks.Where(note => note.IsDoubleKick != strikeTwoX).All(note => note.WasMissed),
                Is.True, "Unplayed kick members must not be fabricated by lane continuation.");
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        });
    }

    [Test]
    public void NonLaneKicksDoNotRefreshCrashLane()
    {
        var chart = Load(moon =>
        {
            AuthorLane(moon, EliteDrumPad.LeftCrash, 0, 0.1, 0.2, 0.5);
            for (int i = 0; i < 6; i++)
                moon.Insert(new MoonNote(TICKS(i * 0.1), (int) EliteDrumPad.Kick));
        });
        var crashes = Members(chart, EliteDrumPad.LeftCrash).ToArray();
        var kicks = Members(chart, EliteDrumPad.Kick).ToArray();
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Single().MemberSources.Count,
            Is.EqualTo(crashes.Length));
        var engine = Create(chart);
        Play(engine, new[] { (crashes[0].Time, EliteDrumsAction.EliteLeftCrash) }
            .Concat(kicks.Select(kick => (kick.Time, EliteDrumsAction.Kick))).ToArray());
        Assert.Multiple(() =>
        {
            Assert.That(kicks.All(note => note.WasHit), Is.True);
            Assert.That(crashes[1].WasHit, Is.True, "Near continuation can resolve on its own lane.");
            Assert.That(crashes[^1].WasMissed, Is.True,
                "Keeping-time kicks cannot sustain a distant crash lane.");
        });
    }

    [Test]
    public void BonhamTripletTwoTomsAndKickLanesRunTogether()
    {
        var chart = Load(moon =>
        {
            AuthorLane(moon, EliteDrumPad.Tom1, SixteenthBeats(0, 8));
            AuthorLane(moon, EliteDrumPad.Tom2, SixteenthBeats(0, 8));
            AuthorLane(moon, EliteDrumPad.Kick, SixteenthBeats(0, 8));
        });
        Assert.That(chart.EliteDrumNativeAuthoredLaneRecords.Select(record =>
                (record.AuthoredPad, record.MemberSources.Count)),
            Is.EquivalentTo(new[] { (EliteDrumPad.Tom1, 33), (EliteDrumPad.Tom2, 33),
                (EliteDrumPad.Kick, 33) }));
        var engine = Create(chart);
        Play(engine, Members(chart, EliteDrumPad.Tom1)
            .Select(note => (note.Time, EliteDrumsAction.EliteTom1))
            .Concat(Members(chart, EliteDrumPad.Tom2)
                .Select(note => (note.Time, EliteDrumsAction.EliteTom2)))
            .Concat(Members(chart, EliteDrumPad.Kick)
                .Select(note => (note.Time, EliteDrumsAction.Kick))).ToArray());
        Assert.Multiple(() =>
        {
            foreach (var pad in new[] { EliteDrumPad.Tom1, EliteDrumPad.Tom2, EliteDrumPad.Kick })
                Assert.That(Members(chart, pad).All(note => note.WasHit), Is.True, pad.ToString());
            Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(99));
            Assert.That(engine.EngineStats.Overhits, Is.Zero);
        });
    }
}
