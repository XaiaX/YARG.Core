using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;
using static YARG.Core.Engine.Drums.DrumsEngineParameters;

namespace YARG.Core.UnitTests.Engine;

public sealed class AlternateGhostScoringTests
{
    private const double CHORD_TIME = 3.25;
    private const double LAST_HIT_TIME = 4;

    [Test]
    public void MissedGhostKeepsComboOnlyWhenEnabled([Values] DrumMode mode,
        [Values] bool alternate, [Values] bool ghostIsParent)
    {
        var engine = Create(mode, alternate, ghostIsParent, DrumNoteType.Ghost);

        for (int i = 0; i < 10; i++) Hit(engine, mode, WarmupTime(i));
        Hit(engine, mode, CHORD_TIME);
        Assert.That(engine.BaseStats.Combo, Is.EqualTo(11));
        Assert.That(engine.BaseStats.ScoreMultiplier, Is.EqualTo(2));
        int scoreBeforeMiss = engine.BaseStats.CommittedScore;

        engine.Update(CHORD_TIME + .25);
        var stats = (DrumsStats)engine.BaseStats;
        Assert.Multiple(() =>
        {
            Assert.That(stats.Combo, Is.EqualTo(alternate ? 11 : 0));
            Assert.That(stats.ScoreMultiplier, Is.EqualTo(alternate ? 2 : 1));
            Assert.That(stats.CommittedScore, Is.EqualTo(scoreBeforeMiss));
            Assert.That(stats.Overhits, Is.Zero);
        });

        Hit(engine, mode, LAST_HIT_TIME);
        Assert.Multiple(() =>
        {
            Assert.That(stats.Combo, Is.EqualTo(alternate ? 12 : 1));
            Assert.That(stats.TotalNotes, Is.EqualTo(13));
            Assert.That(stats.NotesHit, Is.EqualTo(12));
            Assert.That(stats.NotesMissed, Is.EqualTo(1));
            Assert.That(stats.TotalGhosts, Is.EqualTo(1));
            Assert.That(stats.GhostsHit, Is.Zero);
            Assert.That(stats.IsFullCombo, Is.False);
        });
    }

    [Test]
    public void MissedAccentStillBreaksCombo([Values] DrumMode mode)
    {
        var engine = Create(mode, true, false, DrumNoteType.Accent);
        Hit(engine, mode, CHORD_TIME);
        Assert.That(engine.BaseStats.Combo, Is.EqualTo(1));
        engine.Update(CHORD_TIME + .25);
        Assert.That(engine.BaseStats.Combo, Is.Zero);
    }

    private static BaseEngine Create(DrumMode mode, bool alternate, bool ghostIsParent, DrumNoteType type)
    {
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        sync.TimeSignatures.Add(new TimeSignatureChange(4, 4, 0, 0, 0, 0, 0, 0));
        float[] thresholds = [1, 2, 3, 4, 5, 6];
        var parameters = new DrumsEngineParameters(EnginePreset.Default.Drums.HitWindow.Create(), 4, thresholds,
            thresholds, mode, false, false, alternateGhostScoring: alternate);
        if (mode == DrumMode.Elite)
        {
            EliteDrumNote Note(EliteDrumPad pad, double time, DrumNoteType dynamics = DrumNoteType.Neutral) =>
                new(pad, dynamics, EliteDrumsHatState.Indifferent, EliteDrumsHatPedalType.Stomp, false,
                    DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, time, (uint)(time * 960), false);
            var neutral = Note(EliteDrumPad.Snare, CHORD_TIME);
            var dynamicNote = Note(EliteDrumPad.Tom2, CHORD_TIME, type);
            var parent = ghostIsParent ? dynamicNote : neutral;
            parent.AddChildNote(ghostIsParent ? neutral : dynamicNote);
            var notes = Enumerable.Range(0, 10).Select(i => Note(EliteDrumPad.Snare, WarmupTime(i)))
                .Append(parent).Append(Note(EliteDrumPad.Snare, LAST_HIT_TIME)).ToArray();
            Link(notes);
            var chart = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
                new(notes), new(), new());
            return new EliteDrumsEngine(chart, sync, parameters, false, true);
        }
        else
        {
            int red = mode == DrumMode.FiveLane ? (int)FiveLaneDrumPad.Red : (int)FourLaneDrumPad.RedDrum;
            int blue = mode == DrumMode.FiveLane ? (int)FiveLaneDrumPad.Blue : (int)FourLaneDrumPad.BlueDrum;
            DrumNote Note(int pad, double time, DrumNoteType dynamics = DrumNoteType.Neutral) =>
                new(pad, dynamics, DrumNoteFlags.None, NoteFlags.None, time, (uint)(time * 960));
            var neutral = Note(red, CHORD_TIME);
            var dynamicNote = Note(blue, CHORD_TIME, type);
            var parent = ghostIsParent ? dynamicNote : neutral;
            parent.AddChildNote(ghostIsParent ? neutral : dynamicNote);
            var notes = Enumerable.Range(0, 10).Select(i => Note(red, WarmupTime(i)))
                .Append(parent).Append(Note(red, LAST_HIT_TIME)).ToArray();
            Link(notes);
            var instrument = mode switch
            {
                DrumMode.FiveLane => Instrument.FiveLaneDrums,
                DrumMode.ProFourLane => Instrument.ProDrums,
                _ => Instrument.FourLaneDrums
            };
            var chart = new InstrumentDifficulty<DrumNote>(instrument, Difficulty.Expert, new(notes), new(), new());
            return new YargDrumsEngine(chart, sync, parameters, false, false);
        }
    }

    private static void Link<T>(T[] notes) where T : Note<T>
    {
        for (int i = 1; i < notes.Length; i++)
        {
            notes[i - 1].NextNote = notes[i];
            notes[i].PreviousNote = notes[i - 1];
        }
    }

    private static void Hit(BaseEngine engine, DrumMode mode, double time)
    {
        var input = mode == DrumMode.Elite
            ? GameInput.Create(time, EliteDrumsAction.EliteSnare, .5f)
            : GameInput.Create(time, DrumsAction.RedDrum, .5f);
        engine.QueueInput(ref input);
        engine.Update(time);
    }

    private static double WarmupTime(int index) => .5 + index * .25;
}
