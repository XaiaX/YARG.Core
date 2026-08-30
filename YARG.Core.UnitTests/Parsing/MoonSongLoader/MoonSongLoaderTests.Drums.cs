using System.Collections.Generic;
using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Parsing;
using static MoonscraperChartEditor.Song.MoonNote;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Parsing
{
    using static MoonSongLoaderTests;

    public class MoonSongLoaderTests_Drums
    {
        [Test]
        public void DrumMixSetting_ResetsBetweenDifficulties()
        {
            var song = CreateSong();
            var medium = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Medium);
            medium.Add(new MoonText("mix 1 drums0d", 0));
            medium.Add(new MoonNote(TICKS(1), (int) DrumPad.Red));

            var hard = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Hard);
            hard.Add(new MoonNote(TICKS(1), (int) DrumPad.Red));
            hard.Add(new MoonNote(TICKS(2), (int) DrumPad.Yellow));

            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            var track = new MoonSongLoader(song, settings).LoadDrumsTrack(Instrument.FourLaneDrums, null);
            var hardNotes = track.GetDifficulty(Difficulty.Hard).Notes;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hardNotes, Has.Count.EqualTo(2));
                Assert.That(hardNotes[0].Stem, Is.EqualTo(DrumStem.Snare));
                Assert.That(hardNotes[1].Stem, Is.EqualTo(DrumStem.Toms));
            }

        }

        [Test]
        public void NativeBeginnerKickLane_ConvertsKickLaneBoundariesToRegularLaneBoundaries()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Easy);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.ProDrums_KickLane));
            chart.Add(new MoonNote(TICKS(0), (int) DrumPad.Kick));
            chart.Add(new MoonNote(TICKS(1), (int) DrumPad.Kick));
            chart.Add(new MoonNote(TICKS(2), (int) DrumPad.Red));

            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            var track = new MoonSongLoader(song, settings).LoadDrumsTrack(Instrument.ProDrums, null);
            var notes = track.GetDifficulty(Difficulty.Beginner).Notes;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(notes, Has.Count.EqualTo(3));
                Assert.That(notes[0].Pad, Is.EqualTo((int) FourLaneDrumPad.Kick));
                Assert.That(notes[0].IsLaneStart, Is.True);
                Assert.That(notes[1].IsKickLane, Is.True);
                Assert.That(notes[2].IsKickLane, Is.False);
                Assert.That(notes[1].IsLaneEnd, Is.True);
            }
        }

        [Test]
        public void EliteDrumsFallbackKickLane_StampsKickLaneFlags()
        {
            var song = CreateSong();
            foreach (var difficulty in new[] { MoonSong.Difficulty.Easy, MoonSong.Difficulty.Medium, MoonSong.Difficulty.Hard, MoonSong.Difficulty.Expert })
            {
                var eliteSongChart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, difficulty);
                eliteSongChart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_KickLane));
                eliteSongChart.Add(new MoonNote(TICKS(0), (int) EliteDrumNote.EliteDrumPad.Kick));
                eliteSongChart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Kick));
            }

            var eliteSettings = ParseSettings.Default;
            eliteSettings.DrumsType = DrumsType.FourLane;
            var eliteTrack = new MoonSongLoader(song, eliteSettings).LoadEliteDrumsTrack(Instrument.EliteDrums);

            var fallbackSettings = ParseSettings.Default;
            fallbackSettings.DrumsType = DrumsType.FourLane;
            var fallback = new MoonSongLoader(song, fallbackSettings)
                .LoadDrumsTrack(Instrument.ProDrums, eliteTrack);
            using (Assert.EnterMultipleScope())
            {
                foreach (var difficulty in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert, Difficulty.ExpertPlus })
                {
                    var notes = fallback.GetDifficulty(difficulty).Notes;
                    Assert.That(notes, Has.Count.EqualTo(2));
                    Assert.That(notes.All(note => note.IsKickLane), Is.True);
                    Assert.That(notes[0].IsKickLaneStart, Is.True);
                    Assert.That(notes[1].IsKickLaneEnd, Is.True);
                }
            }
        }
    }
}
