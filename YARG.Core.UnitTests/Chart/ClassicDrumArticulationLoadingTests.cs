// pattern: Imperative Shell
using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Parsing;

namespace YARG.Core.UnitTests.Chart;

public sealed class ClassicDrumArticulationLoadingTests
{
    private const DrumNoteFlags FLAM = (DrumNoteFlags)16;
    private const DrumNoteFlags FLAT = (DrumNoteFlags)32;

    private static SongChart Source(bool elite, params MoonNote[] notes)
    {
        var song = new MoonSong(480);
        var moon = song.GetChart(elite ? MoonSong.MoonInstrument.EliteDrums : MoonSong.MoonInstrument.Drums,
            MoonSong.Difficulty.Expert);
        foreach (var note in notes) moon.Add(note);
        var settings = ParseSettings.Default;
        settings.DrumsType = DrumsType.FourLane;
        var loader = new MoonSongLoader(song, settings);
        return new SongChart(480) { SyncTrack = loader.LoadSyncTrack(), AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
    }

    private static ResolvedDrumPlayback State(bool elite, Instrument output) => new(output,
        elite ? DrumSourceFormat.Elite : DrumSourceFormat.FourLane, Difficulty.Expert, Difficulty.Expert,
        DrumExtraKickPolicy.Include, true, ResolvedDrumPlayback.CategoryFor(output, elite ? DrumSourceFormat.Elite : DrumSourceFormat.FourLane));

    [TestCase(Instrument.FourLaneDrums)]
    [TestCase(Instrument.ProDrums)]
    [TestCase(Instrument.FiveLaneDrums)]
    public void EliteHandFlamDownconversion_KeepsOnePadAndTwoUnitArticulation(Instrument output)
    {
        var chart = Source(true, new MoonNote(480, (int)MoonNote.EliteDrumPad.Snare, 0,
            MoonNote.Flags.EliteDrums_Flam | MoonNote.Flags.ProDrums_Accent));
        var notes = DrumPlaybackPreparer.Prepare(chart, State(true, output)).Classic!.Notes;
        Assert.That((1 + notes.Single().ChildNotes.Count), Is.EqualTo(1));
        Assert.That(notes.Single().DrumFlags & FLAM, Is.EqualTo(FLAM));
        Assert.That(notes.Single().Type, Is.EqualTo(DrumNoteType.Accent));
        Assert.That(notes.Single().ConversionOrigin, Is.Not.Null);
    }

    [TestCase(1, 2, 1)]
    [TestCase(2, 3, 2)]
    [TestCase(3, 5, -1)]
    public void MarkedLegacyPairs_RepairOnlyUnambiguousExpansion(int first, int second, int expectedPad)
    {
        var chart = Source(false, new MoonNote(480, first, 0, MoonNote.Flags.EliteDrums_Flam),
            new MoonNote(480, second, 0, MoonNote.Flags.EliteDrums_Flam));
        var notes = DrumPlaybackPreparer.Prepare(chart, State(false, Instrument.ProDrums)).Classic!.Notes;
        if (expectedPad >= 0)
        {
            Assert.That((1 + notes.Single().ChildNotes.Count), Is.EqualTo(1));
            Assert.That(notes.Single().Pad, Is.EqualTo(expectedPad));
            Assert.That(notes.Single().DrumFlags & FLAM, Is.EqualTo(FLAM));
        }
        else
        {
            Assert.That((1 + notes.Single().ChildNotes.Count), Is.EqualTo(2));
            Assert.That(notes.Single().ChildNotes.Prepend(notes.Single()).All(n => (n.DrumFlags & (FLAM | FLAT)) == 0), Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProfileDynamicsModifiers_RetiredKickBitLeavesHandDynamicsAlone(bool global)
    {
        var chart = Source(false,
            new MoonNote(480, (int)MoonNote.DrumPad.Kick, 0, MoonNote.Flags.ProDrums_Accent),
            new MoonNote(960, (int)MoonNote.DrumPad.Red, 0, MoonNote.Flags.ProDrums_Ghost));
        var profile = new YargProfile { GameMode = GameMode.FourLaneDrums, CurrentInstrument = Instrument.ProDrums };
        profile.AddSingleModifier(global ? Modifier.NoDynamics : (Modifier)(1UL << 19));
        var prepared = DrumPlaybackPreparer.Prepare(chart, State(false, Instrument.ProDrums),
            n => profile.ApplyModifiers(n, chart.SyncTrack)).Classic!;
        Assert.That(prepared.Notes[0].Type, Is.EqualTo(DrumNoteType.Neutral));
        Assert.That(prepared.Notes[1].Type, Is.EqualTo(global ? DrumNoteType.Neutral : DrumNoteType.Ghost));
        var source = DrumPlaybackPreparer.Prepare(chart, State(false, Instrument.ProDrums)).Classic!;
        Assert.That(source.Notes[0].Type, Is.EqualTo(DrumNoteType.Neutral));
    }
    [Test]
    public void YellowBlueWithDifferentIdentitiesIsNotAnUnambiguousLegacyExpansion()
    {
        var chart = Source(false,
            new MoonNote(480, 2, 0, MoonNote.Flags.EliteDrums_Flam),
            new MoonNote(480, 3, 0, MoonNote.Flags.EliteDrums_Flam | MoonNote.Flags.ProDrums_Cymbal));
        var notes = DrumPlaybackPreparer.Prepare(chart, State(false, Instrument.ProDrums)).Classic!.Notes;
        Assert.That(notes.Single().ChildNotes.Count, Is.EqualTo(1));
        Assert.That(notes.Single().IsHandFlam, Is.False);
        Assert.That(notes.Single().ChildNotes[0].IsHandFlam, Is.False);
    }

    [Test]
    public void NativeClassicFlamUpconversionRetainsFlatKindAndDynamics()
    {
        var chart = Source(false, new MoonNote(480, 1, 0,
            MoonNote.Flags.EliteDrums_FlatFlam | MoonNote.Flags.ProDrums_Accent));
        var note = DrumPlaybackPreparer.Prepare(chart, State(false, Instrument.EliteDrums)).Elite!.Notes.Single();
        Assert.That(note.IsHandFlam, Is.True);
        Assert.That(note.IsFlatFlam, Is.True);
        Assert.That(note.IsAccent, Is.True);
    }

    [Test]
    public void EffectiveClassicFlamInsideAnAuthoredHandLaneIsOrdinaryAfterResetAndClone()
    {
        var note = new DrumNote(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral,
            DrumNoteFlags.Flam | DrumNoteFlags.FlatFlam, NoteFlags.Tremolo, 1, 480);
        var notes = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert, new() { note }, new(), new());
        DrumPlaybackPreparer.NormalizeClassicFlams(notes);
        var normalized = notes.Notes.Single().Clone();
        normalized.ResetNoteState();
        Assert.That(normalized.IsHandFlam, Is.False);
        Assert.That(normalized.IsFlam || normalized.IsFlatFlam, Is.False);
    }

}