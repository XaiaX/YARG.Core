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
using static MoonscraperChartEditor.Song.MoonNote;
using MoonEliteDrumPad = MoonscraperChartEditor.Song.MoonNote.EliteDrumPad;

namespace YARG.Core.UnitTests.Integration;

[TestFixture]
public sealed class EliteDrumsConversionSelectionEngineIntegrationTests
{
    private const uint Resolution = 480;
    private static readonly float[] StarThresholds = { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f };
    private static readonly float[] SoloThresholds = { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f };

    [Test]
    public void Medium_EliteConversionThenExplicitSelectionPreservesKickChordAndCodaProvenance()
    {
        var fixture = LoadFixture();
        var profile = new YargProfile
        {
            GameMode = GameMode.EliteDrums,
            CurrentInstrument = Instrument.ProDrums,
            CurrentDifficulty = Difficulty.Medium,
            EliteDrumsDownchartTarget = Instrument.ProDrums,
        };

        var selected = DrumDifficultySelector.SelectTrack(fixture.Chart, profile);
        var selectedMedium = selected.GetDifficulty(Difficulty.Medium);
        var medium = selectedMedium.Clone();

        Assert.That(selectedMedium.EliteDrumVisualDescriptors, Is.Empty,
            "ordinary provenance-bearing notes without authored hand-lane phrase provenance expose no visual descriptors");
        Assert.That(selectedMedium.EliteDrumAuthoredLanePhraseRecords, Is.Empty,
            "ordinary conversion without authored hand-lane phrases preserves no phrase records");

        var kick = medium.Notes.Single(note => note.Pad == (int)FourLaneDrumPad.Kick);
        var chord = medium.Notes.Single(note => note.IsChord);
        var coda = medium.Notes[^1];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(selected, Is.SameAs(fixture.Generated),
                "an explicit ProDrums target must select the generated track, not native content");
            Assert.That(medium.Notes, Has.Count.EqualTo(3),
                "the kick, hand-chord parent, and Coda endpoint should survive conversion");
            Assert.That(CountPhysicalNotes(medium.Notes), Is.EqualTo(4),
                "the kick, both physical hand gems, and the Coda endpoint should survive conversion");
            Assert.That(kick.ConversionOrigin, Is.Not.Null);
            Assert.That(kick.ConversionOrigin!.Source.SourceId, Does.StartWith("elite:Medium"));
            var chordNotes = new List<DrumNote>();
            foreach (var note in chord.AllNotes)
            {
                chordNotes.Add(note);
            }
            Assert.That(chordNotes.All(note => note.ConversionOrigin is not null), Is.True);
            Assert.That(chordNotes.Select(note => note.ConversionOrigin!.Source.SourceId),
                Is.All.Not.EqualTo(kick.ConversionOrigin!.Source.SourceId));
            Assert.That(coda.ConversionOrigin!.Source.SourceId, Does.StartWith("elite:Medium"));
            Assert.That(coda.IsCodaEnd, Is.True,
                "the generated runtime note carries the CodaEnd marker even though source identity currently omits source flags");
            Assert.That(medium.Phrases.Any(phrase => phrase.Type == PhraseType.Coda), Is.True);
            Assert.That(medium.Phrases.Any(phrase => phrase.Type == PhraseType.BigRockEnding), Is.True);
        }

        var engine = CreateEngine(medium, fixture.Chart.SyncTrack);
        Hit(engine, kick.Time, DrumsAction.Kick);
        Hit(engine, chord.Time, DrumsAction.RedDrum);
        Hit(engine, chord.Time, DrumsAction.YellowDrum);
        engine.Update(coda.Time + 0.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(kick.WasHit, Is.True, "the selected converted kick must be hittable by a human input");
            var hitChordNotes = true;
            foreach (var note in chord.AllNotes)
            {
                hitChordNotes &= note.WasHit;
            }
            Assert.That(hitChordNotes, Is.True,
                "both physical hand gems in the selected converted chord must be hittable");
            Assert.That(engine.EngineStats.NotesHit, Is.GreaterThanOrEqualTo(3),
                "the real engine must score the kick and both hand gems");
        }
    }

    [Test]
    public void GeneratedBeginnerIntentionallyClearsRuntimeProvenance()
    {
        var fixture = LoadFixture();
        var beginner = fixture.Generated.GetDifficulty(Difficulty.Beginner);

        Assert.That(beginner.Notes, Is.Not.Empty,
            "the Easy Elite source supplies the synthesized Beginner difficulty");
        Assert.That(beginner.Notes.All(note => note.Pad == (int)FourLaneDrumPad.Wildcard), Is.True);
        Assert.That(beginner.Notes.All(note => note.ConversionOrigin is null), Is.True,
            "Beginner intentionally uses wildcard runtime notes and does not retain Elite provenance");
        Assert.That(beginner.EliteDrumVisualDescriptors, Is.Empty,
            "Beginner and legacy-style wildcard output must not expose generated descriptors");
        Assert.That(beginner.EliteDrumAuthoredLanePhraseRecords, Is.Empty,
            "Beginner and legacy-style wildcard output must not expose phrase records");
    }

    private static Fixture LoadFixture()
    {
        var song = new MoonSong(Resolution);
        var easy = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Easy);
        easy.Add(new MoonNote(Tick(0.5), (int)MoonEliteDrumPad.Kick));

        var medium = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Medium);
        medium.Add(new MoonNote(Tick(0.5), (int)MoonEliteDrumPad.Kick));
        medium.Add(new MoonNote(Tick(1.0), (int)MoonEliteDrumPad.Snare));
        medium.Add(new MoonNote(Tick(1.0), (int)MoonEliteDrumPad.Tom1));
        medium.Add(new MoonNote(Tick(1.25), (int)MoonEliteDrumPad.Snare, 0, Flags.CodaEnd));
        medium.Add(new MoonPhrase(Tick(1.0), Tick(1.0), MoonPhrase.Type.Coda));
        medium.Add(new MoonPhrase(Tick(1.5), Tick(0.5), MoonPhrase.Type.BigRockEnding));

        var settings = ParseSettings.Default;
        settings.DrumsType = DrumsType.FourLane;
        var loader = new MoonSongLoader(song, settings);
        var elite = loader.LoadEliteDrumsTrack(Instrument.EliteDrums);
        var generated = loader.LoadEliteDrumsDownchartTrack(Instrument.ProDrums, elite);

        var chart = new SongChart(Resolution)
        {
            SyncTrack = song.syncTrack,
            EliteDrumsDowncharts = new Dictionary<Instrument, InstrumentTrack<DrumNote>>
            {
                [Instrument.ProDrums] = generated,
            },
        };

        return new Fixture(chart, generated);
    }

    private static uint Tick(double seconds) => (uint)(seconds * 2 * Resolution);

    private static int CountPhysicalNotes(IEnumerable<DrumNote> notes)
    {
        var count = 0;
        foreach (var note in notes)
        {
            foreach (var physical in note.AllNotes)
            {
                count++;
            }
        }

        return count;
    }

    private static YargDrumsEngine CreateEngine(InstrumentDifficulty<DrumNote> chart, SyncTrack syncTrack)
    {
        var hitWindow = new HitWindowSettings(0.14, 0.14, 1.0, false, 0, 1.0, 1.0, 0.16, 0.25);
        var parameters = new DrumsEngineParameters(hitWindow, 4, StarThresholds, SoloThresholds,
            DrumsEngineParameters.DrumMode.ProFourLane, false, true,
            DrumsEngineParameters.ELITE_FILL_RULESET_V1, 1f);
        return new YargDrumsEngine(chart, syncTrack, parameters, isBot: false, isMidiDrumsInput: false);
    }

    private static void Hit(YargDrumsEngine engine, double time, DrumsAction action)
    {
        var input = GameInput.Create(time, action, 1f);
        engine.QueueInput(ref input);
        engine.Update(time);
    }

    private sealed record Fixture(SongChart Chart, InstrumentTrack<DrumNote> Generated);
}
