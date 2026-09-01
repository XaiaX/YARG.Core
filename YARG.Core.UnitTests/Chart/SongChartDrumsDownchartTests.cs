using System.Collections.Generic;
using NUnit.Framework;
using YARG.Core.Chart;

namespace YARG.Core.UnitTests.Chart;

public class SongChartDrumsDownchartTests
{
    private static InstrumentTrack<DrumNote> TrackWithNote(Instrument instrument, FourLaneDrumPad pad)
    {
        var notes = new List<DrumNote>
        {
            new(pad, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 0, 0)
        };
        var difficulty = new InstrumentDifficulty<DrumNote>(instrument, Difficulty.Expert, notes, new(), new());
        var track = new InstrumentTrack<DrumNote>(instrument);
        track.AddDifficulty(Difficulty.Expert, difficulty);
        return track;
    }

    [Test]
    public void GetDrumsTrack_PrefersDownchartOnlyWhenRequestedAndAvailable()
    {
        var chart = new SongChart(192u);

        var native = TrackWithNote(Instrument.ProDrums, FourLaneDrumPad.RedDrum);
        chart.ProDrums = native;

        var downchart = TrackWithNote(Instrument.ProDrums, FourLaneDrumPad.BlueDrum);
        chart.EliteDrumsDowncharts = new Dictionary<Instrument, InstrumentTrack<DrumNote>>
        {
            [Instrument.ProDrums] = downchart,
        };

        // Without the downchart request, behavior is exactly as before
        Assert.That(chart.GetDrumsTrack(Instrument.ProDrums), Is.SameAs(native));
        Assert.That(chart.GetDrumsTrack(Instrument.ProDrums, false), Is.SameAs(native));

        // With the request, the downchart variant wins
        Assert.That(chart.GetDrumsTrack(Instrument.ProDrums, true), Is.SameAs(downchart));

        // No downchart variant for this instrument: falls back to the native track
        Assert.That(chart.GetDrumsTrack(Instrument.FourLaneDrums, true), Is.SameAs(chart.FourLaneDrums));
    }
}
