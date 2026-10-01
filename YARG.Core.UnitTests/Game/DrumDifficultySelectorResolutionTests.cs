using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Song;
using YARG.Core.UnitTests.Song;
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

    [TestCase(Instrument.EliteDrums)]
    [TestCase(Instrument.ProDrums)]
    [TestCase(Instrument.FourLaneDrums)]
    [TestCase(Instrument.FiveLaneDrums)]
    public void ScannedNativeCandidateIsSpecificToEachSongsDifficulty(Instrument instrument)
    {
        var parts = AvailableParts.Default;
        switch (instrument)
        {
            case Instrument.EliteDrums: parts.EliteDrums.ActivateSubtrack((int) Difficulty.Hard); break;
            case Instrument.ProDrums: parts.ProDrums.ActivateSubtrack((int) Difficulty.Hard); break;
            case Instrument.FourLaneDrums: parts.FourLaneDrums.ActivateSubtrack((int) Difficulty.Hard); break;
            case Instrument.FiveLaneDrums: parts.FiveLaneDrums.ActivateSubtrack((int) Difficulty.Hard); break;
        }
        var song = new TestSongEntry();
        song.SetParts(parts);

        Assert.That(DrumDifficultySelector.HasNativeEliteCandidate(song), Is.True);
        Assert.That(DrumDifficultySelector.HasNativeEliteCandidate(song, Difficulty.Hard), Is.True);
        Assert.That(DrumDifficultySelector.HasNativeEliteCandidate(song, Difficulty.Expert), Is.False);
    }

    [Test]
    public void MixedShowUsesPerSongNativeCandidatesWithoutSharedFormat()
    {
        var eliteParts = AvailableParts.Default;
        eliteParts.EliteDrums.ActivateSubtrack((int) Requested);
        var eliteSong = new TestSongEntry();
        eliteSong.SetParts(eliteParts);

        var proParts = AvailableParts.Default;
        proParts.ProDrums.ActivateSubtrack((int) Requested);
        var proSong = new TestSongEntry();
        proSong.SetParts(proParts);

        var songs = new[] { eliteSong, proSong };
        Assert.That(songs.All(song => DrumDifficultySelector.HasNativeEliteCandidate(song, Requested)), Is.True);
        Assert.That(songs.Any(song => song.HasInstrument(Instrument.EliteDrums)) &&
            songs.Any(song => song.HasInstrument(Instrument.ProDrums)), Is.True);
        Assert.That(songs.All(song => song.HasInstrument(Instrument.EliteDrums)), Is.False);
        Assert.That(songs.All(song => song.HasInstrument(Instrument.ProDrums)), Is.False);
    }

    [Test]
    public void ScannedCandidateRejectsEmptySongAndNull()
    {
        var song = new TestSongEntry();
        Assert.That(DrumDifficultySelector.HasNativeEliteCandidate(song), Is.False);
        Assert.That(DrumDifficultySelector.HasNativeEliteCandidate(song, Requested), Is.False);
        Assert.That(() => DrumDifficultySelector.HasNativeEliteCandidate(null!),
            Throws.TypeOf<ArgumentNullException>());
        Assert.That(() => DrumDifficultySelector.HasNativeEliteCandidate(null!, Requested),
            Throws.TypeOf<ArgumentNullException>());
    }

    [TestCase(false, false, false, false, null)]
    [TestCase(false, false, false, true, Instrument.FiveLaneDrums)]
    [TestCase(false, false, true, true, Instrument.EliteDrums)]
    [TestCase(false, true, true, true, Instrument.EliteDrums)]
    [TestCase(true, true, true, true, Instrument.EliteDrums)]
    [TestCase(false, true, false, true, Instrument.EliteDrums)]
    [TestCase(false, false, true, false, Instrument.EliteDrums)]
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
            Is.EqualTo(Instrument.EliteDrums));
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

    [TestCase(Instrument.ProDrums)]
    [TestCase(Instrument.FourLaneDrums)]
    public void ConvertedFallbackUsesEliteIdentityWithoutChangingSharedTracks(Instrument sourceInstrument)
    {
        var chart = new SongChart(192);
        AddDrum(chart, sourceInstrument, Requested);
        var profile = EliteProfile();
        profile.CurrentDifficulty = Requested;
        var source = chart.GetDrumsTrack(sourceInstrument).GetDifficulty(Requested);
        var selected = DrumDifficultySelector.SelectNativeEliteTrack(chart, profile).GetDifficulty(Requested);

        Assert.That(selected.Instrument, Is.EqualTo(Instrument.EliteDrums));
        Assert.That(selected.Notes[0].Pad, Is.EqualTo((int) EliteDrumPad.Snare));
        Assert.That(source.Notes[0].Pad, Is.EqualTo((int) FourLaneDrumPad.RedDrum));
        Assert.That(chart.EliteDrums.TryGetDifficulty(Requested, out _), Is.False);
        Assert.That(DrumDifficultySelector.SelectNativeEliteTrack(chart, profile).GetDifficulty(Requested),
            Is.Not.SameAs(selected));
    }

    [Test]
    public void FiveLaneConvertedProTrackIsNotANativeUpconversionSource()
    {
        var chart = new SongChart(192);
        AddDrum(chart, Instrument.ProDrums, Requested);
        AddDrum(chart, Instrument.FiveLaneDrums, Requested);
        chart.ProDrums.IsConvertedDrumsTrack = true;
        Assert.That(chart.ProDrums.Clone().IsConvertedDrumsTrack, Is.True);
        Assert.That(DrumDifficultySelector.ResolveNativeEliteRequest(chart, EliteProfile(), Requested),
            Is.EqualTo(Instrument.FiveLaneDrums));
    }

    [Test]
    public void NativeEliteSelectionWinsOnlyAtTheRequestedDifficulty()
    {
        var chart = new SongChart(192);
        AddElite(chart, Difficulty.Expert);
        AddDrum(chart, Instrument.ProDrums, Requested);
        var profile = EliteProfile();
        profile.CurrentDifficulty = Requested;
        Assert.That(DrumDifficultySelector.SelectNativeEliteTrack(chart, profile).GetDifficulty(Requested)
            .Notes[0].Pad, Is.EqualTo((int) EliteDrumPad.Snare));

        AddElite(chart, Requested);
        Assert.That(DrumDifficultySelector.SelectNativeEliteTrack(chart, profile), Is.SameAs(chart.EliteDrums));
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
