using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Engine;

public sealed class DrumLaneTimestampTests
{
    private sealed class InspectableEngine : YargDrumsEngine
    {
        public double Expiry => LaneAutohitExpireTime;
        public InspectableEngine(InstrumentDifficulty<DrumNote> chart, SyncTrack sync, DrumsEngineParameters parameters)
            : base(chart, sync, parameters, false, false) { }
    }

    [TestCase(1.11)]
    [TestCase(1.16)]
    public void HandLaneRefreshUsesStrikeTimestamp(double previousUpdate)
    {
        var notes = new List<DrumNote>();
        for (int i = 0; i < 8; i++)
        {
            double time = 1 + i * .1;
            notes.Add(new DrumNote(i % 2 == 0 ? FourLaneDrumPad.YellowCymbal : FourLaneDrumPad.BlueCymbal,
                DrumNoteType.Neutral, DrumNoteFlags.None,
                NoteFlags.Trill | (i == 0 ? NoteFlags.LaneStart : 0) | (i == 7 ? NoteFlags.LaneEnd : 0),
                time, (uint)(time * 960), false));
        }
        for (int i = 1; i < notes.Count; i++)
        {
            notes[i - 1].NextNote = notes[i];
            notes[i].PreviousNote = notes[i - 1];
        }
        var chart = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert, notes, new(), new());
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        float[] thresholds = { .06f, .12f, .2f, .45f, .75f, 1.09f };
        var parameters = new DrumsEngineParameters(new HitWindowSettings(.14, .14, 1, false, 0, 1, 1, .2, .1),
            4, thresholds, thresholds, DrumsEngineParameters.DrumMode.ProFourLane, false, true);
        var engine = new InspectableEngine(chart, sync, parameters);
        void Strike(double time, DrumsAction action)
        {
            var input = GameInput.Create(time, action, 1f);
            engine.QueueInput(ref input);
            engine.Update(time);
        }
        engine.Update(0);
        Strike(1, DrumsAction.YellowCymbal);
        Strike(1.1, DrumsAction.BlueCymbal);
        engine.Update(previousUpdate);
        Strike(1.17, DrumsAction.YellowCymbal);
        Assert.That(engine.Expiry, Is.EqualTo(1.37).Within(1e-12));
    }
}
