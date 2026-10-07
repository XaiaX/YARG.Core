using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Parsing;

namespace YARG.Core.UnitTests.Parsing
{
    public class MoonSongLoaderTests_AuthoredDrums
    {
        [TestCase(DrumsType.FourLane, DrumSourceFormat.FourLane, Instrument.ProDrums)]
        [TestCase(DrumsType.FiveLane, DrumSourceFormat.FiveLane, Instrument.FiveLaneDrums)]
        public void ClassicSource_RetainsLowerTierExtraNotesWithoutGeneratedSources(
            DrumsType drumsType, DrumSourceFormat source, Instrument instrument)
        {
            var song = MoonSongLoaderTests.CreateSong();
            var moon = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Easy);
            moon.Add(new MoonNote(0, (int) MoonNote.DrumPad.Kick));
            moon.Add(new MoonNote(100, (int) MoonNote.DrumPad.Kick, 0, MoonNote.Flags.InstrumentPlus));
            moon.Add(new MoonNote(200, (int) MoonNote.DrumPad.Red));
            var settings = ParseSettings.Default;
            settings.DrumsType = drumsType;
            var loader = new MoonSongLoader(song, settings);
            var sources = loader.LoadAuthoredDrumSources();
            Assert.That(sources.Tiers.Count, Is.EqualTo(1));
            Assert.That(sources.TryGetTier(source, Difficulty.Easy, out var tier), Is.True);
            Assert.That(tier!.Facts.OrdinaryKick, Is.True);
            Assert.That(tier.Facts.ExtraKick, Is.True);
            Assert.That(tier.Facts.OtherPlayable, Is.True);
            Assert.That(tier.Facts.PairedKickFlam, Is.False);
            Assert.That(tier.CloneClassicDifficulty().Notes.Count, Is.EqualTo(3));
            var clone = tier.CloneClassicDifficulty();
            clone.Notes.Clear();
            Assert.That(tier.CloneClassicDifficulty().Notes.Count, Is.EqualTo(3));
            Assert.That(loader.LoadDrumsTrack(instrument, null).GetDifficulty(Difficulty.Easy).Notes.Count, Is.EqualTo(2));
        }

        [Test]
        public void EliteSource_RetainsLowerTierExtraAndPairedFacts()
        {
            var song = MoonSongLoaderTests.CreateSong();
            var moon = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Medium);
            var pair = new MoonNote(0, (int) MoonNote.EliteDrumPad.Kick, 0, MoonNote.Flags.EliteDrums_Flam)
            {
                pairedExtraKick = new MoonNote(0, (int) MoonNote.EliteDrumPad.Kick, 0, MoonNote.Flags.InstrumentPlus)
            };
            moon.Add(pair);
            moon.Add(new MoonNote(100, (int) MoonNote.EliteDrumPad.Kick, 0, MoonNote.Flags.InstrumentPlus));
            moon.Add(new MoonNote(200, (int) MoonNote.EliteDrumPad.Snare));
            var loader = new MoonSongLoader(song, ParseSettings.Default);
            var sources = loader.LoadAuthoredDrumSources();
            Assert.That(sources.Tiers.Count, Is.EqualTo(1));
            Assert.That(sources.TryGetTier(DrumSourceFormat.Elite, Difficulty.Medium, out var tier), Is.True);
            Assert.That(tier!.Facts.OrdinaryKick && tier.Facts.ExtraKick && tier.Facts.OtherPlayable && tier.Facts.PairedKickFlam, Is.True);
            Assert.That(tier.PairedKickTicks, Is.EqualTo(new uint[] { 0 }));
            var notes = tier.CloneEliteDifficulty().Notes;
            Assert.That(notes.Count, Is.EqualTo(3));
            Assert.That(notes[0].IsFlam, Is.True);
            Assert.That(notes[1].IsDoubleKick, Is.True);
            Assert.That(loader.LoadEliteDrumsTrack(Instrument.EliteDrums).GetDifficulty(Difficulty.Medium).Notes.Count, Is.EqualTo(2));
        }

        [TestCase(DrumsType.FourLane, DrumSourceFormat.FourLane, Instrument.ProDrums)]
        [TestCase(DrumsType.FiveLane, DrumSourceFormat.FiveLane, Instrument.FiveLaneDrums)]
        public void NativeClassicSource_MatchesHistoricalNotesAndPhrases(
            DrumsType drumsType, DrumSourceFormat source, Instrument instrument)
        {
            var song = MoonSongLoaderTests.CreateSong();
            var moon = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Hard);
            moon.Add(new MoonPhrase(0, 300, MoonPhrase.Type.TremoloLane));
            moon.Add(new MoonNote(0, (int) MoonNote.DrumPad.Red));
            moon.Add(new MoonNote(100, (int) MoonNote.DrumPad.Red));
            moon.Add(new MoonNote(200, (int) MoonNote.DrumPad.Red));
            var settings = ParseSettings.Default;
            settings.DrumsType = drumsType;
            var loader = new MoonSongLoader(song, settings);
            var authored = loader.LoadAuthoredDrumSources().Tiers[(source, Difficulty.Hard)].CloneClassicDifficulty();
            var historical = loader.LoadDrumsTrack(instrument, null).GetDifficulty(Difficulty.Hard);
            Assert.That(authored.Notes.Select(n => (n.Tick, n.Pad, n.IsLaneStart, n.IsLaneEnd)),
                Is.EqualTo(historical.Notes.Select(n => (n.Tick, n.Pad, n.IsLaneStart, n.IsLaneEnd))));
            Assert.That(authored.Phrases.Select(p => (p.Tick, p.TickEnd, p.Type)),
                Is.EqualTo(historical.Phrases.Select(p => (p.Tick, p.TickEnd, p.Type))));
        }

        [Test]
        public void NativeEliteSource_MatchesHistoricalNotesAndRetainsAuthoredLanes()
        {
            var song = MoonSongLoaderTests.CreateSong();
            var moon = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Hard);
            moon.Add(new MoonPhrase(0, 300, MoonPhrase.Type.EliteDrums_SnareLane));
            for (uint tick = 0; tick < 300; tick += 100)
                moon.Add(new MoonNote(tick, (int) MoonNote.EliteDrumPad.Snare));
            var loader = new MoonSongLoader(song, ParseSettings.Default);
            var sources = loader.LoadAuthoredDrumSources();
            var authored = sources.Tiers[(DrumSourceFormat.Elite, Difficulty.Hard)].CloneEliteDifficulty();
            var historical = loader.LoadEliteDrumsTrack(Instrument.EliteDrums).GetDifficulty(Difficulty.Hard);
            Assert.That(authored.Notes.Select(n => (n.Tick, n.Pad, n.IsFlam)),
                Is.EqualTo(historical.Notes.Select(n => (n.Tick, n.Pad, n.IsFlam))));
            Assert.That(authored.EliteDrumNativeAuthoredLaneRecords.Count,
                Is.EqualTo(historical.EliteDrumNativeAuthoredLaneRecords.Count));
            var chart = new SongChart(192);
            chart.Append(new SongChart(192) { AuthoredDrumSources = sources });
            Assert.That(chart.AuthoredDrumSources.Tiers.Count, Is.EqualTo(1));
        }

        [Test]
        public void EmptySources_DoNotInferAuthorshipFromPublicTracks()
        {
            var chart = new SongChart(192);
            Assert.That(chart.AuthoredDrumSources.Tiers, Is.Empty);
            var loader = new MoonSongLoader(MoonSongLoaderTests.CreateSong(), ParseSettings.Default);
            Assert.That(loader.LoadAuthoredDrumSources().Tiers, Is.Empty);
        }
    }
}
