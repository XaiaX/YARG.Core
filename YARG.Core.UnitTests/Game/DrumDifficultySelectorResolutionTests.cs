using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Game;

[TestFixture]
public sealed class DrumDifficultySelectorResolutionTests
{
    private const Difficulty Requested = Difficulty.Hard;

    private static YargProfile EliteProfile() => new()
    {
        GameMode = GameMode.EliteDrums,
        CurrentInstrument = Instrument.EliteDrums,
    };

    private static void AddDrum(SongChart chart, Instrument instrument, Difficulty difficulty,
        bool hasNotes = true)
    {
        var notes = new List<DrumNote>();
        if (hasNotes)
            notes.Add(new DrumNote(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral,
                DrumNoteFlags.None, NoteFlags.None, 0, 0));
        var track = chart.GetDrumsTrack(instrument);
        var part = new InstrumentDifficulty<DrumNote>(instrument, difficulty, notes, new(), new());
        if (!hasNotes)
            part.TextEvents.Add(new TextEvent("event", 0, 0));
        track.AddDifficulty(difficulty, part);
    }

    private static void AddElite(SongChart chart, Difficulty difficulty, bool invisible = false)
    {
        var note = new EliteDrumNote(invisible ? EliteDrumPad.HatPedal : EliteDrumPad.HiHat,
            DrumNoteType.Neutral, EliteDrumsHatState.Closed,
            invisible ? EliteDrumsHatPedalType.InvisibleTerminator : EliteDrumsHatPedalType.Stomp,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None, 0, 0, false);
        chart.EliteDrums.AddDifficulty(difficulty,
            new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, difficulty,
                new List<EliteDrumNote> { note }, new(), new()));
    }

    [TestCase(false, false, false, false, null)]
    [TestCase(false, false, false, true, Instrument.FiveLaneDrums)]
    [TestCase(false, false, true, true, Instrument.FourLaneDrums)]
    [TestCase(false, true, true, true, Instrument.ProDrums)]
    [TestCase(true, true, true, true, Instrument.EliteDrums)]
    [TestCase(false, true, false, true, Instrument.ProDrums)]
    [TestCase(false, false, true, false, Instrument.FourLaneDrums)]
    public void NativePriorityUsesOnlyPlayableRequestedDifficulty(bool elite, bool pro, bool four,
        bool five, Instrument? expected)
    {
        var chart = new SongChart(192);
        if (elite) AddElite(chart, Requested);
        if (pro) AddDrum(chart, Instrument.ProDrums, Requested);
        if (four) AddDrum(chart, Instrument.FourLaneDrums, Requested);
        if (five) AddDrum(chart, Instrument.FiveLaneDrums, Requested);
        Assert.That(DrumDifficultySelector.ResolveNativeEliteRequest(chart, EliteProfile(), Requested),
            Is.EqualTo(expected));
    }

    [TestCase(Instrument.ProDrums)]
    [TestCase(Instrument.FourLaneDrums)]
    public void OtherDifficultyAndEventOnlyTracksCannotWin(Instrument instrument)
    {
        var chart = new SongChart(192);
        AddDrum(chart, instrument, Difficulty.Expert);
        AddDrum(chart, instrument, Requested, false);
        AddDrum(chart, Instrument.FiveLaneDrums, Requested);
        Assert.That(DrumDifficultySelector.ResolveNativeEliteRequest(chart, EliteProfile(), Requested),
            Is.EqualTo(Instrument.FiveLaneDrums));
    }

    [Test]
    public void GeneratedTrackCannotSatisfyAnImplicitNativeRequest()
    {
        var chart = new SongChart(192);
        var generated = new InstrumentTrack<DrumNote>(Instrument.ProDrums);
        generated.AddDifficulty(Requested, new InstrumentDifficulty<DrumNote>(Instrument.ProDrums,
            Requested, new List<DrumNote> { new(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral,
                DrumNoteFlags.None, NoteFlags.None, 0, 0) }, new(), new()));
        chart.EliteDrumsDowncharts = new Dictionary<Instrument, InstrumentTrack<DrumNote>>
        {
            [Instrument.ProDrums] = generated,
        };

        Assert.That(DrumDifficultySelector.ResolveNativeEliteRequest(chart, EliteProfile(), Requested), Is.Null);
    }

    [Test]
    public void InvisibleEliteTerminatorDoesNotSuppressPlayableNativeFallback()
    {
        var chart = new SongChart(192);
        AddElite(chart, Requested, invisible: true);
        AddDrum(chart, Instrument.ProDrums, Requested);
        Assert.That(DrumDifficultySelector.ResolveNativeEliteRequest(chart, EliteProfile(), Requested),
            Is.EqualTo(Instrument.ProDrums));
    }

    [TestCase(Instrument.ProDrums)]
    [TestCase(Instrument.FourLaneDrums)]
    [TestCase(Instrument.FiveLaneDrums)]
    public void ExplicitTargetNeverFallsBackToNativeOrAnotherGeneratedTrack(Instrument target)
    {
        var chart = new SongChart(192);
        AddElite(chart, Requested);
        AddDrum(chart, Instrument.ProDrums, Requested);
        var profile = EliteProfile();
        profile.CurrentInstrument = target;
        profile.EliteDrumsDownchartTarget = target;
        Assert.That(() => DrumDifficultySelector.ResolveNativeEliteRequest(chart, profile, Requested),
            Throws.TypeOf<InvalidDataException>());

        var generated = new InstrumentTrack<DrumNote>(target);
        chart.EliteDrumsDowncharts = new Dictionary<Instrument, InstrumentTrack<DrumNote>>
        {
            [target] = generated,
        };
        Assert.That(() => DrumDifficultySelector.ResolveNativeEliteRequest(chart, profile, Requested),
            Throws.TypeOf<InvalidDataException>());
        generated.AddDifficulty(Requested,
            new InstrumentDifficulty<DrumNote>(target, Requested,
                new List<DrumNote> { new(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral,
                    DrumNoteFlags.None, NoteFlags.None, 0, 0) }, new(), new()));
        Assert.That(DrumDifficultySelector.ResolveNativeEliteRequest(chart, profile, Requested), Is.EqualTo(target));
    }

    [Test]
    public void InvalidTargetOrGameModeFailsClosed()
    {
        var chart = new SongChart(192);
        var profile = EliteProfile();
        profile.EliteDrumsDownchartTarget = (Instrument) 99;
        Assert.That(() => DrumDifficultySelector.ResolveNativeEliteRequest(chart, profile, Requested),
            Throws.TypeOf<InvalidDataException>());
        profile.EliteDrumsDownchartTarget = null;
        profile.GameMode = GameMode.FourLaneDrums;
        Assert.That(() => DrumDifficultySelector.ResolveNativeEliteRequest(chart, profile, Requested),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => DrumDifficultySelector.ResolveNativeEliteRequest(null!, profile, Requested),
            Throws.TypeOf<ArgumentNullException>());
        Assert.That(() => DrumDifficultySelector.ResolveNativeEliteRequest(chart, null!, Requested),
            Throws.TypeOf<ArgumentNullException>());
    }
}
