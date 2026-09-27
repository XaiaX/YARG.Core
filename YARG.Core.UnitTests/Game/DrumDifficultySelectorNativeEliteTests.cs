using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Game;

[TestFixture]
public sealed class DrumDifficultySelectorNativeEliteTests
{
    [Test]
    public void NativeEliteRequiresBothEliteModeAndEliteInstrumentWithoutTarget()
    {
        var chart = new SongChart(192);
        var profile = new YargProfile
        {
            GameMode = GameMode.EliteDrums,
            CurrentInstrument = Instrument.EliteDrums,
        };

        Assert.That(DrumDifficultySelector.UsesNativeEliteTrack(profile), Is.True);
        Assert.That(DrumDifficultySelector.SelectNativeEliteTrack(chart, profile), Is.SameAs(chart.EliteDrums));

        profile.CurrentInstrument = Instrument.ProDrums;
        Assert.That(DrumDifficultySelector.UsesNativeEliteTrack(profile), Is.False);
        Assert.That(() => DrumDifficultySelector.SelectNativeEliteTrack(chart, profile),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(DrumDifficultySelector.SelectTrack(chart, profile), Is.SameAs(chart.ProDrums));

        profile.CurrentInstrument = Instrument.EliteDrums;
        profile.GameMode = GameMode.FourLaneDrums;
        Assert.That(DrumDifficultySelector.UsesNativeEliteTrack(profile), Is.False);
        Assert.That(() => DrumDifficultySelector.SelectNativeEliteTrack(chart, profile),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ExplicitGeneratedTargetAlwaysSelectsGeneratedTrackNotNativeElite()
    {
        var chart = new SongChart(192);
        var generated = new InstrumentTrack<DrumNote>(Instrument.ProDrums);
        chart.EliteDrumsDowncharts = new Dictionary<Instrument, InstrumentTrack<DrumNote>>
        {
            [Instrument.ProDrums] = generated,
        };
        var profile = new YargProfile
        {
            GameMode = GameMode.EliteDrums,
            CurrentInstrument = Instrument.ProDrums,
            EliteDrumsDownchartTarget = Instrument.ProDrums,
        };

        Assert.That(DrumDifficultySelector.UsesNativeEliteTrack(profile), Is.False);
        Assert.That(DrumDifficultySelector.SelectTrack(chart, profile), Is.SameAs(generated));
        Assert.That(() => DrumDifficultySelector.SelectNativeEliteTrack(chart, profile),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ExplicitTargetsNeverFallBackToNativeEliteOrNativeDrums()
    {
        var chart = new SongChart(192);
        var profile = new YargProfile
        {
            GameMode = GameMode.EliteDrums,
            CurrentInstrument = Instrument.ProDrums,
            EliteDrumsDownchartTarget = Instrument.ProDrums,
        };

        Assert.That(() => DrumDifficultySelector.SelectTrack(chart, profile),
            Throws.TypeOf<InvalidDataException>());
        profile.CurrentInstrument = Instrument.EliteDrums;
        Assert.That(DrumDifficultySelector.UsesNativeEliteTrack(profile), Is.False);
        Assert.That(() => DrumDifficultySelector.SelectTrack(chart, profile),
            Throws.TypeOf<InvalidDataException>());
        profile.EliteDrumsDownchartTarget = (Instrument) 99;
        Assert.That(() => DrumDifficultySelector.SelectTrack(chart, profile),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void NullArgumentsFailBeforeSelection()
    {
        var chart = new SongChart(192);
        var profile = new YargProfile { GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.EliteDrums };
        Assert.That(() => DrumDifficultySelector.UsesNativeEliteTrack(null!), Throws.TypeOf<ArgumentNullException>());
        Assert.That(() => DrumDifficultySelector.SelectNativeEliteTrack(null!, profile), Throws.TypeOf<ArgumentNullException>());
        Assert.That(() => DrumDifficultySelector.SelectNativeEliteTrack(chart, null!), Throws.TypeOf<ArgumentNullException>());
    }
}
