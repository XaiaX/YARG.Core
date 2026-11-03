// pattern: Imperative Shell
using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Parsing;

namespace YARG.Core.UnitTests.Chart;

public sealed class KickFlamLoadingTests
{
    private static SongChart Source(bool elite, params MoonNote[] notes)
    {
        var song = new MoonSong(480);
        var moon = song.GetChart(elite ? MoonSong.MoonInstrument.EliteDrums : MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Expert);
        foreach (var note in notes) moon.Add(note);
        var settings = ParseSettings.Default; settings.DrumsType = DrumsType.FourLane;
        var loader = new MoonSongLoader(song, settings);
        return new SongChart(480) { SyncTrack = loader.LoadSyncTrack(), AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
    }
    private static ResolvedDrumPlayback State(bool elite, Instrument output) => new(output,
        elite ? DrumSourceFormat.Elite : DrumSourceFormat.FourLane, Difficulty.Expert, Difficulty.Expert,
        DrumExtraKickPolicy.Include, true, ResolvedDrumPlayback.CategoryFor(output, elite ? DrumSourceFormat.Elite : DrumSourceFormat.FourLane));

    [TestCase(Instrument.FourLaneDrums, false)]
    [TestCase(Instrument.ProDrums, false)]
    [TestCase(Instrument.FiveLaneDrums, false)]
    [TestCase(Instrument.FourLaneDrums, true)]
    [TestCase(Instrument.ProDrums, true)]
    [TestCase(Instrument.FiveLaneDrums, true)]
    public void ClassicOutputKeepsKickFlamAndHandsButNeverKickDynamics(Instrument output, bool elite)
    {
        var kick = elite ? (int)MoonNote.EliteDrumPad.Kick : (int)MoonNote.DrumPad.Kick;
        var snare = elite ? (int)MoonNote.EliteDrumPad.Snare : (int)MoonNote.DrumPad.Red;
        var chart = Source(elite,
            new MoonNote(480, kick, 0, MoonNote.Flags.EliteDrums_Flam | MoonNote.Flags.ProDrums_Accent),
            new MoonNote(480, snare, 0, MoonNote.Flags.ProDrums_Ghost),
            new MoonNote(960, kick, 0, MoonNote.Flags.InstrumentPlus | MoonNote.Flags.ProDrums_Ghost));
        var live = DrumPlaybackPreparer.Prepare(chart, State(elite, output)).Classic!;
        var notes = live.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
        Assert.That(notes, Has.Length.EqualTo(3));
        var first = notes.Single(n => n.Pad == 0 && n.Tick == 480);
        Assert.That(first.IsFlam, Is.True);
        Assert.That(first.IsFlatFlam, Is.False);
        Assert.That(notes.Where(n => n.Pad == 0).All(n => n.IsNeutral), Is.True);
        Assert.That(notes.Single(n => n.Pad != 0).IsGhost, Is.True);
        if (elite)
        {
            var native = DrumPlaybackPreparer.Prepare(chart, State(true, Instrument.EliteDrums)).Elite!;
            Assert.That(native.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Single(n => n.Tick == 480 && n.Pad == (int)EliteDrumNote.EliteDrumPad.Kick).IsAccent, Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FlatKickIntentNormalizesToRegularWithoutErasingTheFlam(bool elite)
    {
        var chart = Source(elite, new MoonNote(480, elite ? (int)MoonNote.EliteDrumPad.Kick : 0, 0,
            MoonNote.Flags.EliteDrums_Flam | MoonNote.Flags.EliteDrums_FlatFlam));
        var prepared = DrumPlaybackPreparer.Prepare(chart, State(elite, Instrument.ProDrums)).Classic!.Notes.Single();
        Assert.That(prepared.IsFlam, Is.True);
        Assert.That(prepared.IsFlatFlam, Is.False);
        var cloned = prepared.Clone(); cloned.ResetNoteState();
        Assert.That(cloned.IsFlam, Is.True);
        Assert.That(cloned.IsFlatFlam, Is.False);
    }

    [Test]
    public void V1ReplayKeepsNativeKickDynamicsAndNoKickFlam()
    {
        var chart = Source(false, new MoonNote(480, 0, 0, MoonNote.Flags.EliteDrums_Flam | MoonNote.Flags.ProDrums_Accent));
        var v1 = DrumPlaybackPreparer.Prepare(chart, State(false, Instrument.ProDrums), classicArticulationRuleset: 1).Classic!.Notes.Single();
        Assert.That(v1.IsAccent, Is.True);
        Assert.That(v1.IsFlam, Is.False);
        var live = DrumPlaybackPreparer.Prepare(chart, State(false, Instrument.ProDrums)).Classic!.Notes.Single();
        Assert.That(live.IsNeutral, Is.True);
        Assert.That(live.IsFlam, Is.True);
        Assert.That(v1.IsAccent, Is.True, "live preparation must not mutate the historical copy");
    }

    [Test]
    public void ClassicModesDoNotOfferRetiredKickDynamicsModifier()
    {
        var retired = (Modifier)(1UL << 19);
        foreach (var mode in new[] { GameMode.FourLaneDrums, GameMode.FiveLaneDrums, GameMode.EliteDrums })
            foreach (var instrument in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums, Instrument.EliteDrums })
                Assert.That(mode.PossibleModifiers(instrument).possible & retired, Is.EqualTo(Modifier.None), $"{mode}/{instrument}");
    }
}
