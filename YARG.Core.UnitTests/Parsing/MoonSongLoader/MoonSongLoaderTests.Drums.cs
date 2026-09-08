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
        public void NativeBeginnerKickLane_FollowsUpstreamWildcardLaneSemantics()
        {
            // Upstream (dev) DrumsFinalPass treats every lane phrase on Beginner as a
            // wildcard tremolo: kick-lane boundaries are converted to regular wildcard
            // LaneStart/LaneEnd only when the phrase contains enough hand notes to form
            // a valid tremolo. A pure-kick phrase produces no lane at all. (The YOLO
            // line used to stamp kick-lane markers on Beginner directly; that behavior
            // was superseded by the upstream rework.)
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
                // Beginner derives from Easy, but every pad is unconditionally wildcard.
                Assert.That(notes, Has.Count.EqualTo(3));
                Assert.That(notes[0].Pad, Is.EqualTo((int) FourLaneDrumPad.Wildcard));
                Assert.That(notes[1].Pad, Is.EqualTo((int) FourLaneDrumPad.Wildcard));
                Assert.That(notes[2].Pad, Is.EqualTo((int) FourLaneDrumPad.Wildcard));

                // Kick-lane phrases are converted to wildcard lane markers.
                Assert.That(notes[0].IsLaneStart, Is.True);
                Assert.That(notes[1].IsKickLane, Is.False);
                Assert.That(notes[1].IsLaneEnd, Is.False);
                Assert.That(notes[2].IsLaneEnd, Is.True);
            }

            // With two hand notes in the phrase, the kick-lane boundaries ARE
            // converted to regular wildcard lane boundaries.
            var song2 = CreateSong();
            var chart2 = song2.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Easy);
            chart2.Add(new MoonPhrase(TICKS(0), TICKS(4), MoonPhrase.Type.ProDrums_KickLane));
            chart2.Add(new MoonNote(TICKS(0), (int) DrumPad.Kick));
            chart2.Add(new MoonNote(TICKS(1), (int) DrumPad.Kick));
            chart2.Add(new MoonNote(TICKS(2), (int) DrumPad.Red));
            chart2.Add(new MoonNote(TICKS(3), (int) DrumPad.Red));

            var track2 = new MoonSongLoader(song2, settings).LoadDrumsTrack(Instrument.ProDrums, null);
            var notes2 = track2.GetDifficulty(Difficulty.Beginner).Notes;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(notes2, Has.Count.EqualTo(4));
                // Beginner pads remain wildcard even when the source phrase contains kicks.
                Assert.That(notes2[0].Pad, Is.EqualTo((int) FourLaneDrumPad.Wildcard));
                Assert.That(notes2[1].Pad, Is.EqualTo((int) FourLaneDrumPad.Wildcard));
                Assert.That(notes2[2].Pad, Is.EqualTo((int) FourLaneDrumPad.Wildcard));
                Assert.That(notes2[3].Pad, Is.EqualTo((int) FourLaneDrumPad.Wildcard));
                Assert.That(notes2[0].IsLaneStart, Is.True);
                Assert.That(notes2[1].IsLaneStart, Is.False);
                Assert.That(notes2[2].IsLaneStart, Is.False);
                Assert.That(notes2[3].IsLaneEnd, Is.True);
                Assert.That(notes2[2].IsKickLane, Is.False);
                Assert.That(notes2[3].IsKickLane, Is.False);
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

        [Test]
        public void ForcedEliteDrumsDownchart_ReturnsDownchartEvenWhenNativeChartExists()
        {
            var song = CreateSong();

            // A native four-lane chart, which normally wins over the Elite fallback
            var nativeChart = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Expert);
            nativeChart.Add(new MoonNote(TICKS(1), (int) DrumPad.Red));

            foreach (var difficulty in new[] { MoonSong.Difficulty.Easy, MoonSong.Difficulty.Medium, MoonSong.Difficulty.Hard, MoonSong.Difficulty.Expert })
            {
                var eliteSongChart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, difficulty);
                eliteSongChart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Snare));
                eliteSongChart.Add(new MoonNote(TICKS(2), (int) EliteDrumNote.EliteDrumPad.Snare));
            }

            var eliteSettings = ParseSettings.Default;
            eliteSettings.DrumsType = DrumsType.FourLane;
            var eliteTrack = new MoonSongLoader(song, eliteSettings).LoadEliteDrumsTrack(Instrument.EliteDrums);

            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            settings.EliteDrumsDownchartOutputs = new[] { Instrument.ProDrums };
            var loader = new MoonSongLoader(song, settings);

            // The native track is unaffected by the downchart request
            var native = loader.LoadDrumsTrack(Instrument.ProDrums, eliteTrack);
            Assert.That(native.GetDifficulty(Difficulty.Expert).Notes, Has.Count.EqualTo(1));

            // The forced downchart is generated even though the native chart exists
            var downcharts = loader.LoadEliteDrumsDownchartTracks(eliteTrack);
            Assert.That(downcharts, Does.ContainKey(Instrument.ProDrums));

            var downchart = downcharts[Instrument.ProDrums];
            using (Assert.EnterMultipleScope())
            {
                foreach (var difficulty in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert })
                {
                    var notes = downchart.GetDifficulty(difficulty).Notes;
                    Assert.That(notes, Has.Count.EqualTo(2));
                    Assert.That(notes[0].Pad, Is.EqualTo((int) FourLaneDrumPad.RedDrum));
                    Assert.That(notes[1].Pad, Is.EqualTo((int) FourLaneDrumPad.RedDrum));
                }
            }
        }

        [Test]
        public void ForcedEliteDrumsDownchart_FiveLaneOutput_UsesFiveLanePads()
        {
            var song = CreateSong();

            var eliteSongChart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            eliteSongChart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Snare));

            var eliteSettings = ParseSettings.Default;
            eliteSettings.DrumsType = DrumsType.FourLane;
            var eliteTrack = new MoonSongLoader(song, eliteSettings).LoadEliteDrumsTrack(Instrument.EliteDrums);

            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            settings.EliteDrumsDownchartOutputs = new[] { Instrument.FiveLaneDrums };
            var loader = new MoonSongLoader(song, settings);

            var downcharts = loader.LoadEliteDrumsDownchartTracks(eliteTrack);
            Assert.That(downcharts, Does.ContainKey(Instrument.FiveLaneDrums));

            var notes = downcharts[Instrument.FiveLaneDrums].GetDifficulty(Difficulty.Expert).Notes;
            Assert.That(notes, Has.Count.EqualTo(1));
            Assert.That(notes[0].Pad, Is.EqualTo((int) FiveLaneDrumPad.Red));
        }

        [Test]
        public void ForcedEliteDrumsDownchart_NoEliteChart_ReturnsNoDowncharts()
        {
            var song = CreateSong();

            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            settings.EliteDrumsDownchartOutputs = new[] { Instrument.ProDrums };
            var loader = new MoonSongLoader(song, settings);

            var downcharts = loader.LoadEliteDrumsDownchartTracks(
                new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums));

            Assert.That(downcharts, Is.Empty);
        }

        [Test]
        public void ForcedEliteDrumsDownchart_HatPedalOnlyChart_BuildsNoDownchart()
        {
            // The loader half of the rules/loader agreement (scan half:
            // EliteDrumsDownchartScanTests): an Elite chart whose every note is an
            // unforced hat pedal downcharts to nothing, so no downchart variant is
            // exposed at all — the menu rules must not offer a target for such a song
            // when it has no native drums either.
            var song = CreateSong();
            var eliteSongChart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            eliteSongChart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.HatPedal));
            eliteSongChart.Add(new MoonNote(TICKS(2), (int) EliteDrumNote.EliteDrumPad.HatPedal));

            var downcharts = LoadDowncharts(song);
            Assert.That(downcharts, Is.Empty,
                "an Elite chart of only unforced hat pedals must generate no downchart");

            // The implicit native fallback path agrees: with no native drums and no
            // convertible notes, the drums track loads empty difficulties.
            var eliteTrack = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);
            var native = new MoonSongLoader(song, ParseSettings.Default)
                .LoadDrumsTrack(Instrument.FourLaneDrums, eliteTrack);
            Assert.That(native.GetDifficulty(Difficulty.Expert).Notes, Is.Empty,
                "the implicit elite fallback must not invent notes either");
        }

        [Test]
        public void ForcedEliteDrumsDownchart_InvisibleTerminatorHatPedalOnlyChart_BuildsNoDownchart()
        {
            // Same shape, but the hat pedals carry the invisible-terminator flag
            // (ghost velocity in MIDI): they are dropped even when channel flagged,
            // so the downchart is empty and the preparser mask must stay empty too.
            var song = CreateSong();
            var eliteSongChart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            eliteSongChart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.HatPedal, 0,
                Flags.EliteDrums_InvisibleTerminator | Flags.EliteDrums_ChannelFlagYellow));
            eliteSongChart.Add(new MoonNote(TICKS(2), (int) EliteDrumNote.EliteDrumPad.HatPedal, 0,
                Flags.EliteDrums_InvisibleTerminator));

            var downcharts = LoadDowncharts(song);
            Assert.That(downcharts, Is.Empty,
                "invisible-terminator hat pedals must never keep a downchart alive, even when channel flagged");
        }

        [Test]
        public void ForcedEliteDrumsDownchart_ChannelFlaggedHatPedalOnlyChart_BuildsADownchart()
        {
            // The mirror image: channel-flagged hat pedals convert to cymbal gems, so
            // the downchart exists — and the scan-time mask must record it (see
            // EliteDrumsDownchartScanTests.ChannelFlaggedHatPedal_CountsAsDownchart).
            var song = CreateSong();
            var eliteSongChart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            eliteSongChart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.HatPedal, 0,
                Flags.EliteDrums_ChannelFlagYellow));

            var downcharts = LoadDowncharts(song);
            Assert.That(downcharts, Does.ContainKey(Instrument.ProDrums));
            Assert.That(downcharts[Instrument.ProDrums].GetDifficulty(Difficulty.Expert).Notes, Is.Not.Empty,
                "a channel-flagged hat pedal converts to a cymbal gem");
        }

        [Test]
        public void ForcedEliteDrumsDownchart_SuppressedHatPedalChord_ConvertsOnlyTheHiHat()
        {
            // The loader half of the chord-context regression (scan half:
            // EliteDrumsDownchartScanTests.ChordedFlaggedHatPedalWithPlainHiHat_AdvertisesOnlyViaTheHiHat):
            // the full reader suppresses a channel-flagged pedal chorded with a
            // non-indifferent hi-hat into an invisible terminator, and the downchart
            // builder drops that pedal — but the hi-hat partner itself converts to a
            // yellow cymbal, so the difficulty stays playable and both sides must
            // advertise it. A "suppressed pedals only" chart cannot exist: the
            // suppression requires the chorded hi-hat, which converts.
            var song = CreateSong();
            var eliteSongChart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            eliteSongChart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.HatPedal, 0,
                Flags.EliteDrums_InvisibleTerminator | Flags.EliteDrums_ChannelFlagYellow));
            eliteSongChart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.HiHat));

            var downcharts = LoadDowncharts(song);
            Assert.That(downcharts, Does.ContainKey(Instrument.ProDrums),
                "the chorded hi-hat keeps the downchart alive");

            var notes = downcharts[Instrument.ProDrums].GetDifficulty(Difficulty.Expert).Notes;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(notes, Has.Count.EqualTo(1),
                    "the suppressed pedal must not convert; only the hi-hat's gem remains");
                Assert.That(notes[0].Pad, Is.EqualTo((int) FourLaneDrumPad.YellowCymbal),
                    "the hi-hat converts to a yellow cymbal gem");
            }
        }

        [Test]
        public void ForcedEliteDrumsDownchart_InvalidTargetsAreSkippedAndFailClosed()
        {
            // MINOR regression: downchart output requests can come from serialized
            // data (e.g. replay profiles), so malformed or non-drum values must be
            // skipped before loading / fail closed at the loader boundary instead
            // of reaching ToNativeGameMode() and throwing.
            var song = CreateSong();
            var eliteSongChart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            eliteSongChart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Snare));

            var eliteTrack = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);

            var malformedTarget = unchecked((Instrument) 1234);
            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            settings.EliteDrumsDownchartOutputs = new[]
            {
                Instrument.FiveFretGuitar, // well-formed, but not a drums target
                malformedTarget,           // malformed serialized value
                Instrument.EliteDrums,     // drums, but not a downchart output format
                Instrument.ProDrums,       // valid target
            };
            var loader = new MoonSongLoader(song, settings);

            var downcharts = loader.LoadEliteDrumsDownchartTracks(eliteTrack);
            Assert.That(downcharts.Keys, Is.EqualTo(new[] { Instrument.ProDrums }),
                "only the valid target may be built; invalid ones are skipped, not thrown");

            // The single-track boundary fails closed instead of throwing
            var malformed = loader.LoadEliteDrumsDownchartTrack(malformedTarget, eliteTrack);
            Assert.That(malformed.IsEmpty, Is.True,
                "an invalid target yields an empty track so callers fall back to the native track");

            // Valid target and native fallback behavior are preserved
            Assert.That(downcharts[Instrument.ProDrums].GetDifficulty(Difficulty.Expert).Notes, Is.Not.Empty);
            var native = loader.LoadDrumsTrack(Instrument.ProDrums, eliteTrack);
            Assert.That(native.GetDifficulty(Difficulty.Expert).Notes, Is.Not.Empty,
                "the native fallback path is unaffected by invalid downchart outputs");
        }

        private static IReadOnlyDictionary<Instrument, InstrumentTrack<DrumNote>> LoadDowncharts(MoonSong song)
        {
            var eliteTrack = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);

            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            settings.EliteDrumsDownchartOutputs = new[] { Instrument.ProDrums };
            return new MoonSongLoader(song, settings).LoadEliteDrumsDownchartTracks(eliteTrack);
        }
    }
}
