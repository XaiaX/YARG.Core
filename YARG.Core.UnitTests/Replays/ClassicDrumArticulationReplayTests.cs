// pattern: Imperative Shell
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using MoonscraperChartEditor.Song;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Game;
using YARG.Core.IO;
using YARG.Core.Input;
using YARG.Core.Parsing;
using YARG.Core.Replays;

namespace YARG.Core.UnitTests.Replays;

public sealed class ClassicDrumArticulationReplayTests
{
    private static DrumsEngineParameters Parameters(byte ruleset = 1) => new(
        new HitWindowSettings(.1, .1, 1, false, 0, 1, 1, .15, .25), 4,
        new[] { 1f, 2, 3, 4, 5, 6 }, new[] { 1f, 2, 3, 4, 5, 6 },
        DrumsEngineParameters.DrumMode.ProFourLane, false, true, classicArticulationRuleset: ruleset);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void RecordedRulesetRoundTrips_AndOldPayloadsDefaultToLegacy(int ruleset)
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) Parameters((byte)ruleset).Serialize(writer);
        var bytes = memory.ToArray();
        using var current = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(current.Span);
        var stream = new FixedArrayStream(current);
        Assert.That(new DrumsEngineParameters(ref stream, ReplayIO.REPLAY_VERSIONS.CURRENT).ClassicArticulationRuleset,
            Is.EqualTo(ruleset));
        using var old = FixedArray<byte>.Alloc(bytes.Length - 4);
        bytes.AsSpan(0, bytes.Length - 4).CopyTo(old.Span);
        var oldStream = new FixedArrayStream(old);
        Assert.That(new DrumsEngineParameters(ref oldStream, 22).ClassicArticulationRuleset, Is.Zero);
    }

    [Test]
    public void NativeClassicHistoricalPreparationRetainsBothLegacyHandsAndNeutralKick()
    {
        var song = new MoonSong(480);
        var raw = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Expert);
        raw.Add(new MoonNote(480, 1, 0, MoonNote.Flags.EliteDrums_Flam));
        raw.Add(new MoonNote(480, 2, 0, MoonNote.Flags.EliteDrums_Flam));
        raw.Add(new MoonNote(960, 0, 0, MoonNote.Flags.ProDrums_Accent));
        var settings = ParseSettings.Default;
        settings.DrumsType = DrumsType.FourLane;
        var loader = new MoonSongLoader(song, settings);
        var chart = new SongChart(480) { SyncTrack = loader.LoadSyncTrack(), AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
        var state = new ResolvedDrumPlayback(Instrument.ProDrums, DrumSourceFormat.FourLane,
            Difficulty.Expert, Difficulty.Expert, DrumExtraKickPolicy.Include, true, ResolvedDrumPlayback.CategoryFor(Instrument.ProDrums, DrumSourceFormat.FourLane));
        var old = DrumPlaybackPreparer.Prepare(chart, state, classicArticulationRuleset: 0).Classic!;
        var live = DrumPlaybackPreparer.Prepare(chart, state).Classic!;
        Assert.Multiple(() =>
        {
            Assert.That(old.Notes[0].ChildNotes.Count, Is.EqualTo(1));
            Assert.That(old.Notes[0].IsHandFlam, Is.False);
            Assert.That(old.Notes[1].Type, Is.EqualTo(DrumNoteType.Neutral));
            Assert.That(live.Notes[0].ChildNotes.Count, Is.Zero);
            Assert.That(live.Notes[0].IsHandFlam, Is.True);
            Assert.That(live.Notes[1].Type, Is.EqualTo(DrumNoteType.Neutral));
        });
    }

    [Test]
    public void HistoricalEliteConversionUsesOriginalAdjacentExpansionAndOrigins()
    {
        var song = new MoonSong(480);
        song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert)
            .Add(new MoonNote(480, (int)MoonNote.EliteDrumPad.Snare, 0, MoonNote.Flags.EliteDrums_Flam));
        var loader = new MoonSongLoader(song, ParseSettings.Default);
        var chart = new SongChart(480) { SyncTrack = loader.LoadSyncTrack(), AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
        var state = new ResolvedDrumPlayback(Instrument.ProDrums, DrumSourceFormat.Elite,
            Difficulty.Expert, Difficulty.Expert, DrumExtraKickPolicy.Include, true,
            ResolvedDrumPlayback.CategoryFor(Instrument.ProDrums, DrumSourceFormat.Elite));
        var old = DrumPlaybackPreparer.Prepare(chart, state, classicArticulationRuleset: 0).Classic!;
        var live = DrumPlaybackPreparer.Prepare(chart, state).Classic!;
        Assert.That(old.Notes[0].ChildNotes.Count, Is.EqualTo(1));
        Assert.That(old.Notes[0].ChildNotes.Prepend(old.Notes[0]).Select(n => n.ConversionOrigin!.ExpansionOrdinal), Is.EqualTo(new[] { 0, 1 }));
        var restored = DrumPlaybackPreparer.PrepareClassicRuleset(chart, live, 0);
        Assert.That(restored.Notes[0].ChildNotes.Count, Is.EqualTo(1));
        Assert.That(live.Notes[0].ChildNotes.Count, Is.Zero);
    }
    [Test]
    public void HistoricalUnresolvedExpertPlusKeepsRecordedTierAndExtraKicks()
    {
        var song = new MoonSong(480);
        var raw = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
        raw.Add(new MoonNote(480, (int)MoonNote.EliteDrumPad.Snare, 0, MoonNote.Flags.EliteDrums_Flam));
        raw.Add(new MoonNote(960, (int)MoonNote.EliteDrumPad.Kick));
        raw.Add(new MoonNote(1440, (int)MoonNote.EliteDrumPad.Kick, 0, MoonNote.Flags.InstrumentPlus));
        var loader = new MoonSongLoader(song, ParseSettings.Default);
        var chart = new SongChart(480) { SyncTrack = loader.LoadSyncTrack(), AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
        var state = new ResolvedDrumPlayback(Instrument.ProDrums, DrumSourceFormat.Elite,
            Difficulty.Expert, Difficulty.Expert, DrumExtraKickPolicy.Include, true,
            ResolvedDrumPlayback.CategoryFor(Instrument.ProDrums, DrumSourceFormat.Elite));
        var live = DrumPlaybackPreparer.Prepare(chart, state).Classic!;
        var selected = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.ExpertPlus,
            live.Notes, live.Phrases, live.TextEvents);
        var old = DrumPlaybackPreparer.PrepareClassicRuleset(chart, selected, 0);
        Assert.That(old.Difficulty, Is.EqualTo(Difficulty.ExpertPlus));
        Assert.That(old.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Count(n => n.Pad == 0), Is.EqualTo(2));
        Assert.That(old.Notes[0].ChildNotes.Count, Is.EqualTo(1));
    }

    [Test]
    public void KickOnlyModifierAndRulesetSurviveReplaySerialization()
    {
        var profile = new YargProfile(Guid.NewGuid())
        {
            CurrentInstrument = Instrument.ProDrums, PreferredInstrument = Instrument.ProDrums,
            CurrentDifficulty = Difficulty.Expert, GameMode = GameMode.FourLaneDrums,
        };
        profile.AddSingleModifier(Modifier.NoKickDynamics);
        var frame = new ReplayFrame(profile, Parameters(), new DrumsStats(), Array.Empty<GameInput>());
        var data = new ReplayData(new Dictionary<Guid, ColorProfile>(), new Dictionary<Guid, CameraPreset>(),
            new Dictionary<Guid, RockMeterPreset>(), true, new[] { frame }, Array.Empty<double>());
        var bytes = data.Serialize().ToArray();
        using var array = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(array.Span);
        var loaded = new ReplayData(new FixedArrayStream(array), ReplayIO.REPLAY_VERSIONS.CURRENT, new ReplayReadOptions());
        Assert.That(loaded.Frames[0].Profile.IsModifierActive(Modifier.NoKickDynamics), Is.True);
        Assert.That(loaded.Frames[0].Profile.IsModifierActive(Modifier.NoDynamics), Is.False);
        Assert.That(((DrumsEngineParameters)loaded.Frames[0].EngineParameters).ClassicArticulationRuleset, Is.EqualTo(1));
    }

    [Test]
    public void UnsupportedRulesetIsRejectedAtParameterBoundary()
    {
        Assert.Throws<InvalidDataException>(() => Parameters(3));
    }

    [TestCase(Instrument.ProDrums, false)]
    [TestCase(Instrument.EliteDrums, false)]
    [TestCase(Instrument.ProDrums, true)]
    [TestCase(Instrument.EliteDrums, true)]
    public void LegacyEliteExtraKickIgnoresNewVelocityBeforeKickPolicyNormalization(Instrument output, bool normalize)
    {
        var song = new MoonSong(480);
        var raw = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
        raw.Add(new MoonNote(480, (int)MoonNote.EliteDrumPad.Kick, 0,
            MoonNote.Flags.InstrumentPlus | MoonNote.Flags.ProDrums_Ghost));
        if (!normalize) raw.Add(new MoonNote(960, (int)MoonNote.EliteDrumPad.Kick, 0, MoonNote.Flags.ProDrums_Accent));
        var loader = new MoonSongLoader(song, ParseSettings.Default);
        var chart = new SongChart(480) { SyncTrack = loader.LoadSyncTrack(), AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
        var state = new ResolvedDrumPlayback(output, DrumSourceFormat.Elite, Difficulty.Expert, Difficulty.Expert,
            normalize ? DrumExtraKickPolicy.NormalizeExtraOnly : DrumExtraKickPolicy.Include, !normalize,
            ResolvedDrumPlayback.CategoryFor(output, DrumSourceFormat.Elite));
        var old = DrumPlaybackPreparer.Prepare(chart, state, classicArticulationRuleset: 0);
        var live = DrumPlaybackPreparer.Prepare(chart, state);
        Assert.That(old.Classic?.Notes[0].Type ?? old.Elite!.Notes[0].Dynamics, Is.EqualTo(DrumNoteType.Neutral));
        Assert.That(live.Classic?.Notes[0].Type ?? live.Elite!.Notes[0].Dynamics, Is.EqualTo(output == Instrument.EliteDrums ? DrumNoteType.Ghost : DrumNoteType.Neutral));
        if (!normalize)
            Assert.That(old.Classic?.Notes[1].Type ?? old.Elite!.Notes[1].Dynamics, Is.EqualTo(DrumNoteType.Accent));
        else
            Assert.That(old.Classic?.Notes[0].IsDoubleKick ?? old.Elite!.Notes[0].IsDoubleKick, Is.False);
    }

    [Test]
    public void UnresolvedHistoricalNativeEliteExtraKickIsNeutralOnAnOwnedCopy()
    {
        var song = new MoonSong(480);
        var raw = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
        raw.Add(new MoonNote(480, (int)MoonNote.EliteDrumPad.Kick, 0,
            MoonNote.Flags.InstrumentPlus | MoonNote.Flags.ProDrums_Ghost));
        raw.Add(new MoonNote(960, (int)MoonNote.EliteDrumPad.Kick, 0, MoonNote.Flags.ProDrums_Accent));
        var loader = new MoonSongLoader(song, ParseSettings.Default);
        var chart = new SongChart(480) { SyncTrack = loader.LoadSyncTrack(), EliteDrums = loader.LoadEliteDrumsTrack(Instrument.EliteDrums),
            AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
        var profile = new YargProfile { GameMode = GameMode.EliteDrums, CurrentInstrument = Instrument.EliteDrums,
            CurrentDifficulty = Difficulty.ExpertPlus };
        var old = DrumDifficultySelector.SelectNativeEliteTrack(chart, profile, 0).GetDifficulty(Difficulty.ExpertPlus);
        Assert.That(old.Notes[0].Dynamics, Is.EqualTo(DrumNoteType.Neutral));
        Assert.That(old.Notes[1].Dynamics, Is.EqualTo(DrumNoteType.Accent));
        Assert.That(chart.EliteDrums.GetDifficulty(Difficulty.ExpertPlus).Notes[0].Dynamics, Is.EqualTo(DrumNoteType.Ghost));
        var live = DrumPlaybackPreparer.Prepare(chart, new ResolvedDrumPlayback(Instrument.ProDrums, DrumSourceFormat.Elite,
            Difficulty.Expert, Difficulty.Expert, DrumExtraKickPolicy.Include, true,
            ResolvedDrumPlayback.CategoryFor(Instrument.ProDrums, DrumSourceFormat.Elite))).Classic!;
        var selected = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.ExpertPlus,
            live.Notes, live.Phrases, live.TextEvents);
        var oldClassic = DrumPlaybackPreparer.PrepareClassicRuleset(chart, selected, 0);
        Assert.That(oldClassic.Notes[0].Type, Is.EqualTo(DrumNoteType.Neutral));
        Assert.That(oldClassic.Notes[1].Type, Is.EqualTo(DrumNoteType.Accent));
    }

}
