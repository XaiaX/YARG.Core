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
            // Beginner notes use wildcard pads, and kick-lane boundaries become regular
            // wildcard lane markers.
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
        public void NativeAuthoredDrumLanes_StampFinalFlagsAndBoundaries()
        {
            var song = CreateSong();
            var expert = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Expert);
            expert.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.TremoloLane));
            expert.Add(new MoonNote(TICKS(0), (int) DrumPad.Red));
            expert.Add(new MoonNote(TICKS(1), (int) DrumPad.Red));
            expert.Add(new MoonNote(TICKS(2), (int) DrumPad.Red));

            var hard = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Hard);
            hard.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.TrillLane));
            hard.Add(new MoonNote(TICKS(0), (int) DrumPad.Red));
            hard.Add(new MoonNote(TICKS(1), (int) DrumPad.Yellow));
            hard.Add(new MoonNote(TICKS(2), (int) DrumPad.Red));

            var medium = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Medium);
            medium.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.ProDrums_KickLane));
            medium.Add(new MoonNote(TICKS(0), (int) DrumPad.Kick));
            medium.Add(new MoonNote(TICKS(1), (int) DrumPad.Kick));
            medium.Add(new MoonNote(TICKS(2), (int) DrumPad.Kick));

            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            var track = new MoonSongLoader(song, settings).LoadDrumsTrack(Instrument.ProDrums, null);
            var tremolo = track.GetDifficulty(Difficulty.Expert).Notes;
            var trill = track.GetDifficulty(Difficulty.Hard).Notes;
            var kick = track.GetDifficulty(Difficulty.Medium).Notes;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tremolo.All(note => note.IsTremolo), Is.True);
                Assert.That(tremolo[0].IsLaneStart, Is.True);
                Assert.That(tremolo[^1].IsLaneEnd, Is.True);
                Assert.That(trill.All(note => note.IsTrill), Is.True);
                Assert.That(trill[0].IsLaneStart, Is.True);
                Assert.That(trill[^1].IsLaneEnd, Is.True);
                Assert.That(kick.All(note => note.IsKickLane), Is.True);
                Assert.That(kick[0].IsKickLaneStart, Is.True);
                Assert.That(kick[^1].IsKickLaneEnd, Is.True);
            }
        }

        [Test]
        public void EliteDrumsDownchart_AuthoredSnareLaneWithThreeSurvivorsStampsLaneForAllTargets()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_SnareLane));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(2), (int)EliteDrumNote.EliteDrumPad.Snare));

            var downcharts = LoadDowncharts(song);
            foreach (var target in new[] { Instrument.ProDrums, Instrument.FourLaneDrums, Instrument.FiveLaneDrums })
            {
                var difficulty = downcharts[target].GetDifficulty(Difficulty.Expert);
                using (Assert.EnterMultipleScope())
                {
                    // The authored lane identity and interval are retained; the phrase is
                    // never collapsed into a native tremolo phrase.
                    Assert.That(difficulty.Phrases.Count(phrase => phrase.Type == PhraseType.EliteDrums_SnareLane), Is.EqualTo(1));
                    Assert.That(difficulty.Phrases.Any(phrase => phrase.Type == PhraseType.TremoloLane), Is.False);

                    // Three surviving authored members form one runtime lane.
                    Assert.That(difficulty.Notes.All(note => note.IsTremolo), Is.True);
                    Assert.That(difficulty.Notes[0].IsLaneStart, Is.True);
                    Assert.That(difficulty.Notes[^1].IsLaneEnd, Is.True);
                    Assert.That(difficulty.Phrases.Any(phrase => phrase.Type == PhraseType.DrumFill), Is.False,
                        "generated Elite fill descriptors must not synthesize native fill phrases");

                    // Stage 2: the phrase resolves to the TRUE final output pad of its
                    // surviving final children and publishes exactly one descriptor.
                    var physical = ExpandNotes(difficulty.Notes).ToArray();
                    var expectedPad = target is Instrument.FiveLaneDrums
                        ? (int) FiveLaneDrumPad.Red
                        : (int) FourLaneDrumPad.RedDrum;
                    var expectedFinalPad = new EliteDrumFinalPadIdentity(target, expectedPad);
                    var descriptors = difficulty.EliteDrumVisualDescriptors;
                    Assert.That(descriptors, Has.Count.EqualTo(1));
                    Assert.That(descriptors[0].FinalPad, Is.EqualTo(expectedFinalPad));
                    Assert.That(descriptors[0].IsFinalIdentityResolved, Is.True);
                    // Every actual final child pad equals the descriptor FinalPad exactly.
                    Assert.That(physical.Select(note => note.Pad).Distinct().ToArray(),
                        Is.EqualTo(new[] { expectedPad }));
                    Assert.That(descriptors[0].Origins, Has.Count.EqualTo(3));
                    var phraseRecords = difficulty.EliteDrumAuthoredLanePhraseRecords;
                    Assert.That(phraseRecords, Has.Count.EqualTo(1));
                    Assert.That(phraseRecords[0].FinalPad, Is.EqualTo(expectedFinalPad));
                    Assert.That(phraseRecords[0].AuthoredStartTick, Is.EqualTo(TICKS(0)));
                    Assert.That(phraseRecords[0].AuthoredEndTick, Is.EqualTo(TICKS(3)));
                    Assert.That(phraseRecords[0].SourceIds, Has.Count.EqualTo(3));
                    Assert.That(phraseRecords[0].Origins, Has.Count.EqualTo(3));
                }
            }
        }

        [Test]
        public void EliteDrumsDownchart_AuthoredRideLaneResolvesIntermediateCymbalPerTarget()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_RideLane));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Ride));
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Ride));
            chart.Add(new MoonNote(TICKS(2), (int)EliteDrumNote.EliteDrumPad.Ride));

            var downcharts = LoadDowncharts(song);
            // The authored Ride is a Blue intermediate with a cymbal marker. The FINAL
            // output pad differs per target creation mapping, and the descriptor always
            // carries the actual final children pad of its own target:
            //   Pro: BlueCymbal (cymbal marking retained)
            //   FourLane: BlueDrum (plain drums)
            //   FiveLane (serial through the Pro representation): Orange
            foreach (var (target, expectedPad) in new[]
                     {
                         (Instrument.ProDrums, (int) FourLaneDrumPad.BlueCymbal),
                         (Instrument.FourLaneDrums, (int) FourLaneDrumPad.BlueDrum),
                         (Instrument.FiveLaneDrums, (int) FiveLaneDrumPad.Orange),
                     })
            {
                var difficulty = downcharts[target].GetDifficulty(Difficulty.Expert);
                var physical = ExpandNotes(difficulty.Notes).ToArray();
                var expectedFinalPad = new EliteDrumFinalPadIdentity(target, expectedPad);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(physical.Select(note => note.Pad).Distinct().ToArray(),
                        Is.EqualTo(new[] { expectedPad }), $"target {target}");
                    var descriptors = difficulty.EliteDrumVisualDescriptors;
                    Assert.That(descriptors, Has.Count.EqualTo(1), $"target {target}");
                    Assert.That(descriptors[0].FinalPad, Is.EqualTo(expectedFinalPad), $"target {target}");
                    Assert.That(descriptors[0].IsFinalIdentityResolved, Is.True, $"target {target}");
                    // The authored Ride pad (8) never leaks into the final identity.
                    Assert.That(descriptors[0].FinalPad.Pad,
                        Is.Not.EqualTo((int) EliteDrumNote.EliteDrumPad.Ride), $"target {target}");
                }
            }
        }

        [Test]
        public void EliteDrumsDownchart_AuthoredRightCrashLaneResolvesGreenSourcePerTarget()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_RightCrashLane));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.RightCrash));
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.RightCrash));
            chart.Add(new MoonNote(TICKS(2), (int)EliteDrumNote.EliteDrumPad.RightCrash));

            var downcharts = LoadDowncharts(song);
            foreach (var (target, expectedPad) in new[]
                     {
                         (Instrument.ProDrums, (int) FourLaneDrumPad.GreenCymbal),
                         (Instrument.FourLaneDrums, (int) FourLaneDrumPad.GreenDrum),
                         (Instrument.FiveLaneDrums, (int) FiveLaneDrumPad.Orange),
                     })
            {
                var difficulty = downcharts[target].GetDifficulty(Difficulty.Expert);
                var physical = ExpandNotes(difficulty.Notes).ToArray();
                var expectedFinalPad = new EliteDrumFinalPadIdentity(target, expectedPad);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(physical.Select(note => note.Pad).Distinct().ToArray(),
                        Is.EqualTo(new[] { expectedPad }), $"target {target}");
                    var descriptors = difficulty.EliteDrumVisualDescriptors;
                    Assert.That(descriptors, Has.Count.EqualTo(1), $"target {target}");
                    Assert.That(descriptors[0].FinalPad, Is.EqualTo(expectedFinalPad), $"target {target}");
                    Assert.That(descriptors[0].Origins, Has.Count.EqualTo(3), $"target {target}");
                }
            }
        }

        [Test]
        public void EliteDrumsDownchart_OverlappingHandPhrasesOwnOnlyTheirAuthoredPad()
        {
            var song = CreateSong();
            var source = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            var lanes = new[]
            {
                (MoonPhrase.Type.EliteDrums_HiHatLane, EliteDrumNote.EliteDrumPad.HiHat),
                (MoonPhrase.Type.EliteDrums_LeftCrashLane, EliteDrumNote.EliteDrumPad.LeftCrash),
                (MoonPhrase.Type.EliteDrums_RightCrashLane, EliteDrumNote.EliteDrumPad.RightCrash),
            };
            foreach (var (type, _) in lanes)
                source.Add(new MoonPhrase(TICKS(0), TICKS(9), type));
            for (var i = 0; i < 9; i++)
                source.Add(new MoonNote(TICKS(i), (int) lanes[i % lanes.Length].Item2));

            var difficulty = LoadDowncharts(song)[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);
            var physical = ExpandNotes(difficulty.Notes).ToArray();
            var records = difficulty.EliteDrumAuthoredLanePhraseRecords;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(records, Has.Count.EqualTo(3));
                Assert.That(records.All(record => record.IsFinalIdentityResolved), Is.True);
                Assert.That(records.Select(record => record.FinalPad.Pad).Distinct().Count(), Is.EqualTo(3));
                foreach (var (type, pad) in lanes)
                {
                    var record = records.Single(record => record.GameplayId.Contains(type.ToString()));
                    var members = physical.Where(note => record.Origins.Contains(note.ConversionOrigin!)).ToArray();
                    Assert.That(members, Has.Length.EqualTo(3), type.ToString());
                    Assert.That(members.All(note => note.ConversionOrigin!.Source.Pad == (int) pad), Is.True,
                        $"{type} must not claim the other simultaneous authored phrases' gems");
                }
            }
        }

        [Test]
        public void EliteDrumsDownchart_SerialFiveLaneChordExceptionResolvesCymbalChordToYellowAndOrange()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_RideLane));
            // Three Ride+RightCrash chords: each chord resolves to a BlueCymbal + GreenCymbal
            // pair, and the existing serial FiveLane chord exception remaps the BlueCymbal
            // gem to Yellow while the GreenCymbal gem stays Orange.
            for (var beat = 0; beat < 3; beat++)
            {
                chart.Add(new MoonNote(TICKS(beat), (int)EliteDrumNote.EliteDrumPad.Ride));
                chart.Add(new MoonNote(TICKS(beat), (int)EliteDrumNote.EliteDrumPad.RightCrash));
            }

            var difficulty = LoadDowncharts(song)[Instrument.FiveLaneDrums].GetDifficulty(Difficulty.Expert);
            var physical = ExpandNotes(difficulty.Notes).ToArray();
            var descriptors = difficulty.EliteDrumVisualDescriptors;

            using (Assert.EnterMultipleScope())
            {
                // The Ride phrase owns Ride gems only; the interleaved RightCrash gems
                // remain physical notes but do not become members of the Ride lane.
                Assert.That(physical, Has.Length.EqualTo(6));
                Assert.That(physical.Count(note => note.Pad == (int) FiveLaneDrumPad.Yellow), Is.EqualTo(3));
                Assert.That(physical.Count(note => note.Pad == (int) FiveLaneDrumPad.Orange), Is.EqualTo(3));

                Assert.That(descriptors, Has.Count.EqualTo(1));
                Assert.That(descriptors[0].FinalPad.Pad, Is.EqualTo((int) FiveLaneDrumPad.Yellow));
                Assert.That(descriptors[0].IsFinalIdentityResolved, Is.True);
                Assert.That(descriptors[0].Origins, Has.Count.EqualTo(3));
                // Every descriptor's FinalPad equals its actual final children pads.
                foreach (var descriptor in descriptors)
                {
                    var memberPads = physical
                        .Where(note => descriptor.Origins.Contains(note.ConversionOrigin!))
                        .Select(note => note.Pad)
                        .Distinct()
                        .ToArray();
                    Assert.That(memberPads, Is.EqualTo(new[] { descriptor.FinalPad.Pad }));
                }
            }
        }

        [Test]
        public void EliteDrumsDownchart_FiveLaneEntryPointsConvergeAndNativeFiveLaneStaysUntouched()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_RideLane));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Ride));
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Ride));
            chart.Add(new MoonNote(TICKS(2), (int)EliteDrumNote.EliteDrumPad.Ride));

            // Elite notes are always serially converted through the Four Lane/Pro
            // representation, even when the five-lane track is requested through the
            // native load path: both entry points must converge on the same true final
            // identity (Orange), never on the intermediate Blue or the authored Ride.
            var eliteTrack = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);
            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FiveLane;
            settings.EliteDrumsDownchartOutputs = new[] { Instrument.FiveLaneDrums };
            var loader = new MoonSongLoader(song, settings);
            var generated = loader.LoadEliteDrumsDownchartTracks(eliteTrack)[Instrument.FiveLaneDrums];
            var fallback = loader.LoadDrumsTrack(Instrument.FiveLaneDrums, eliteTrack);
            var expectedFinalPad = new EliteDrumFinalPadIdentity(Instrument.FiveLaneDrums,
                (int) FiveLaneDrumPad.Orange);

            using (Assert.EnterMultipleScope())
            {
                foreach (var (label, difficulty) in new[] { ("generated", generated.GetDifficulty(Difficulty.Expert)),
                             ("fallback", fallback.GetDifficulty(Difficulty.Expert)) })
                {
                    var physical = ExpandNotes(difficulty.Notes).ToArray();
                    Assert.That(physical.Select(note => note.Pad).Distinct().ToArray(),
                        Is.EqualTo(new[] { (int) FiveLaneDrumPad.Orange }), label);
                    var descriptors = difficulty.EliteDrumVisualDescriptors;
                    Assert.That(descriptors, Has.Count.EqualTo(1), label);
                    Assert.That(descriptors[0].FinalPad, Is.EqualTo(expectedFinalPad), label);
                    Assert.That(descriptors[0].VisualIdentity.AppearanceModelKey,
                        Is.EqualTo(nameof(FiveLaneDrumPad.Orange)), label);
                }
            }

            // Native five-lane data keeps its literal colored outputs and carries no
            // conversion provenance, so no descriptors are produced for it.
            var nativeSong = CreateSong();
            var native = nativeSong.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Expert);
            native.Add(new MoonNote(TICKS(0), (int) DrumPad.Blue, 0, Flags.ProDrums_Cymbal));
            var nativeTrack = new MoonSongLoader(nativeSong, settings).LoadDrumsTrack(Instrument.FiveLaneDrums, null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(nativeTrack.GetDifficulty(Difficulty.Expert).Notes[0].Pad,
                    Is.EqualTo((int) FiveLaneDrumPad.Blue));
                Assert.That(nativeTrack.GetDifficulty(Difficulty.Expert).EliteDrumVisualDescriptors,
                    Is.Empty);
            }
        }

        [Test]
        public void EliteDrumsDownchart_MalformedAuthoredLaneWithTwoSurvivorsGetsNoLaneAndNoDescriptor()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_SnareLane));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Snare));

            var downcharts = LoadDowncharts(song);
            foreach (var target in new[] { Instrument.ProDrums, Instrument.FourLaneDrums, Instrument.FiveLaneDrums })
            {
                var difficulty = downcharts[target].GetDifficulty(Difficulty.Expert);
                using (Assert.EnterMultipleScope())
                {
                    // 0/1/2 surviving members is malformed: the authored provenance is
                    // retained on the notes, but no runtime lane or descriptor exists.
                    Assert.That(difficulty.Notes, Has.Count.EqualTo(2));
                    Assert.That(difficulty.Notes.All(note => note.ConversionOrigin is not null), Is.True);
                    Assert.That(difficulty.Notes.Any(note => note.IsTremolo), Is.False);
                    Assert.That(difficulty.Notes.Any(note => note.IsLaneStart || note.IsLaneEnd), Is.False);
                    Assert.That(difficulty.EliteDrumVisualDescriptors, Is.Empty);
                }
            }
        }

        [Test]
        public void EliteDrumsDownchart_TouchingAuthoredLanesSplitMembershipAtBoundaryTick()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            // Touching intervals: [0,2) and [2,5). The half-open authored membership
            // gives the boundary gem at tick 2 to the second phrase only.
            chart.Add(new MoonPhrase(TICKS(0), TICKS(2), MoonPhrase.Type.EliteDrums_SnareLane));
            chart.Add(new MoonPhrase(TICKS(2), TICKS(3), MoonPhrase.Type.EliteDrums_SnareLane));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(2), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(3), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(4), (int)EliteDrumNote.EliteDrumPad.Snare));

            var difficulty = LoadDowncharts(song)[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(difficulty.Phrases.Count(phrase => phrase.Type == PhraseType.EliteDrums_SnareLane), Is.EqualTo(2));

                // First phrase: only 2 members (the boundary gem belongs to the second
                // phrase), so it is malformed and gets no lane.
                var firstTwo = difficulty.Notes.Where(note => note.Tick < TICKS(2)).ToArray();
                Assert.That(firstTwo.All(note => note.ConversionOrigin is not null), Is.True);
                Assert.That(firstTwo.Any(note => note.IsTremolo || note.IsLaneStart || note.IsLaneEnd), Is.False);

                // Second phrase: 3 members including the boundary gem, so it forms one lane.
                var lastThree = difficulty.Notes.Where(note => note.Tick >= TICKS(2)).ToArray();
                Assert.That(lastThree, Has.Length.EqualTo(3));
                Assert.That(lastThree.All(note => note.IsTremolo), Is.True);
                Assert.That(lastThree[0].IsLaneStart, Is.True);
                Assert.That(lastThree[^1].IsLaneEnd, Is.True);

                // The valid second phrase resolves and publishes: one descriptor whose
                // final identity equals its survivors' actual final pads.
                var expectedFinalPad = new EliteDrumFinalPadIdentity(Instrument.ProDrums,
                    (int) FourLaneDrumPad.RedDrum);
                var descriptors = difficulty.EliteDrumVisualDescriptors;
                Assert.That(descriptors, Has.Count.EqualTo(1));
                Assert.That(descriptors[0].FinalPad, Is.EqualTo(expectedFinalPad));
                Assert.That(descriptors[0].IsFinalIdentityResolved, Is.True);
                Assert.That(descriptors[0].AuthoredStartTick, Is.EqualTo(TICKS(2)));
                Assert.That(descriptors[0].AuthoredEndTick, Is.EqualTo(TICKS(5)));
                var phraseRecords = difficulty.EliteDrumAuthoredLanePhraseRecords;
                Assert.That(phraseRecords, Has.Count.EqualTo(1));
                Assert.That(phraseRecords[0].FinalPad, Is.EqualTo(expectedFinalPad));
                Assert.That(phraseRecords[0].AuthoredStartTick, Is.EqualTo(TICKS(2)));
                Assert.That(phraseRecords[0].AuthoredEndTick, Is.EqualTo(TICKS(5)));
            }
        }

        [Test]
        public void EliteDrumsDownchart_FlamExpansionsCountAsSurvivingPhysicalMembers()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(4), MoonPhrase.Type.EliteDrums_SnareLane));
            // Two authored gems, one of them a flam: the flam expands to two final
            // physical children, so the phrase has 3 surviving physical members.
            var flam = new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Snare);
            flam.flags = MoonNote.Flags.EliteDrums_Flam;
            chart.Add(flam);
            chart.Add(new MoonNote(TICKS(2), (int)EliteDrumNote.EliteDrumPad.Snare));

            var difficulty = LoadDowncharts(song)[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);
            var physical = ExpandNotes(difficulty.Notes).ToArray();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(physical, Has.Length.EqualTo(3));
                Assert.That(physical.Select(note => note.ConversionOrigin!.ExpansionOrdinal).OrderBy(ordinal => ordinal),
                    Is.EqualTo(new[] { 0, 0, 1 }));
                Assert.That(physical.All(note => note.IsTremolo), Is.True);
                Assert.That(physical.Count(note => note.IsLaneStart), Is.EqualTo(1));
                Assert.That(physical.Count(note => note.IsLaneEnd), Is.EqualTo(1));

                // Stage 2: the flam splits the survivors across RedDrum x2 + YellowDrum x1;
                // no group reaches three members, so the phrase is malformed: it stays
                // explicitly unresolved and publishes nothing.
                var descriptors = difficulty.EliteDrumVisualDescriptors;
                Assert.That(descriptors, Is.Empty);
                var phraseRecords = difficulty.EliteDrumAuthoredLanePhraseRecords;
                Assert.That(phraseRecords, Has.Count.EqualTo(1));
                Assert.That(phraseRecords[0].FinalPad.IsUnresolved, Is.True);
                Assert.That(phraseRecords[0].IsFinalIdentityResolved, Is.False);
                Assert.That(phraseRecords[0].Origins, Has.Count.EqualTo(3));
                Assert.That(phraseRecords[0].SourceIds, Has.Count.EqualTo(2));
            }
        }

        [Test]
        public void EliteDrumsDownchart_AuthoredRollFlamExpandsOnlyInGeneratedDrums()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(4), MoonPhrase.Type.EliteDrums_SnareLane));
            var flam = new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Snare);
            flam.flags = MoonNote.Flags.EliteDrums_Flam;
            chart.Add(flam);

            var eliteTrack = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);
            var native = eliteTrack.GetDifficulty(Difficulty.Expert).Notes.Single();
            var generated = LoadDowncharts(song)[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);
            var physical = ExpandNotes(generated.Notes).ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(native.IsFlam, Is.False);
                Assert.That(native.IsAuthoredFlam, Is.True);
                Assert.That(native.ChannelFlag, Is.EqualTo(EliteDrumsChannelFlag.None));
                Assert.That(physical, Has.Length.EqualTo(2));
                Assert.That(physical.Select(note => note.ConversionOrigin!.ExpansionOrdinal).OrderBy(ordinal => ordinal),
                    Is.EqualTo(new[] { 0, 1 }));
                Assert.That(physical.Select(note => note.Pad).Distinct().ToArray(), Has.Length.EqualTo(2));
            }
        }

        [Test]
        public void EliteDrumsDownchart_CollisionShuntedGemsRemainSurvivingLaneMembers()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(4), MoonPhrase.Type.EliteDrums_Tom1Lane));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Tom1));
            // Tom1 and HiHat both resolve to yellow at tick 1, so the collision resolver
            // shunts the tom to red. Shunted gems still survive and still count.
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.HiHat));
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Tom1));
            chart.Add(new MoonNote(TICKS(3), (int)EliteDrumNote.EliteDrumPad.Tom1));

            var difficulty = LoadDowncharts(song)[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);
            var physical = ExpandNotes(difficulty.Notes).ToArray();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(physical, Has.Length.EqualTo(4));
                // Three Tom1 members survived, including the shunted tom; the
                // interleaved HiHat is not a member of this Tom1 phrase.
                Assert.That(physical.All(note => note.ConversionOrigin is not null), Is.True);
                Assert.That(physical.Count(note => note.IsTremolo), Is.EqualTo(3));
                Assert.That(physical.Single(note => note.ConversionOrigin!.Source.Pad ==
                    (int) EliteDrumNote.EliteDrumPad.HiHat).IsTremolo, Is.False);
                Assert.That(physical.Count(note => note.IsLaneStart), Is.EqualTo(1));
                Assert.That(physical.Count(note => note.IsLaneEnd), Is.EqualTo(1));

                // Stage 2: the shunt spreads the three Tom1 survivors across RedDrum x1
                // and YellowDrum x2 — neither group reaches the minimum, so the phrase
                // is unresolved and nothing is published.
                var descriptors = difficulty.EliteDrumVisualDescriptors;
                Assert.That(descriptors, Is.Empty);
                var phraseRecords = difficulty.EliteDrumAuthoredLanePhraseRecords;
                Assert.That(phraseRecords, Has.Count.EqualTo(1));
                Assert.That(phraseRecords[0].FinalPad.IsUnresolved, Is.True);
                Assert.That(phraseRecords[0].IsFinalIdentityResolved, Is.False);
                Assert.That(phraseRecords[0].Origins, Has.Count.EqualTo(3));
            }
        }

        [Test]
        public void EliteDrumsDownchart_KickGemsInsideAuthoredHandLaneAreNotLaneMembers()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            // Creep-shape fill: snare lane phrase with interposed kicks. Kick gems are
            // excluded from V1 hand-lane membership, so only the four snare gems count.
            chart.Add(new MoonPhrase(TICKS(0), TICKS(6), MoonPhrase.Type.EliteDrums_SnareLane));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Kick));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Kick));
            chart.Add(new MoonNote(TICKS(2), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(4), (int)EliteDrumNote.EliteDrumPad.Kick));
            chart.Add(new MoonNote(TICKS(4), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(5), (int)EliteDrumNote.EliteDrumPad.Snare));

            var difficulty = LoadDowncharts(song)[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);
            var physical = ExpandNotes(difficulty.Notes).ToArray();
            var snares = physical.Where(note => note.Pad == (int)FourLaneDrumPad.RedDrum).ToArray();
            var kicks = physical.Where(note => note.Pad == (int)FourLaneDrumPad.Kick).ToArray();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(snares, Has.Length.EqualTo(4));
                Assert.That(kicks, Has.Length.EqualTo(3));

                // The four snare members form the lane; the kicks never join it.
                Assert.That(snares.All(note => note.IsTremolo), Is.True);
                Assert.That(snares.Count(note => note.IsLaneStart), Is.EqualTo(1));
                Assert.That(snares.Count(note => note.IsLaneEnd), Is.EqualTo(1));
                Assert.That(kicks.Any(note => note.IsTremolo || note.IsLaneStart || note.IsLaneEnd), Is.False);

                // Stage 2: the phrase publishes one descriptor resolved to its actual
                // final children pad (Pro RedDrum), covering exactly the snare origins.
                var expectedFinalPad = new EliteDrumFinalPadIdentity(Instrument.ProDrums,
                    (int) FourLaneDrumPad.RedDrum);
                var descriptors = difficulty.EliteDrumVisualDescriptors;
                Assert.That(descriptors, Has.Count.EqualTo(1));
                Assert.That(descriptors[0].FinalPad, Is.EqualTo(expectedFinalPad));
                Assert.That(descriptors[0].IsFinalIdentityResolved, Is.True);
                Assert.That(descriptors[0].FinalPad.Pad, Is.EqualTo((int) FourLaneDrumPad.RedDrum));
                Assert.That(descriptors[0].Origins, Has.Count.EqualTo(4));
                Assert.That(descriptors[0].Origins.Any(origin =>
                        origin.Source.Pad == (int) EliteDrumNote.EliteDrumPad.Kick),
                    Is.False, "kick sources leaked into the published lane descriptor");

                var phraseRecords = difficulty.EliteDrumAuthoredLanePhraseRecords;
                Assert.That(phraseRecords, Has.Count.EqualTo(1));
                Assert.That(phraseRecords[0].SourceIds, Has.Count.EqualTo(4));
                Assert.That(phraseRecords[0].Origins, Has.Count.EqualTo(4));
                Assert.That(phraseRecords[0].FinalPad, Is.EqualTo(expectedFinalPad));
            }
        }

        [Test]
        public void EliteDrumsDownchart_RetainsKickLaneBigRockEndingAndCodaEnd()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_KickLane));
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.BigRockEnding));
            chart.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.Coda));
            chart.Add(new MoonNote(TICKS(0), (int) EliteDrumNote.EliteDrumPad.Kick));
            chart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Kick, 0, Flags.CodaEnd));
            chart.Add(new MoonNote(TICKS(2), (int) EliteDrumNote.EliteDrumPad.Kick));

            var difficulty = LoadDowncharts(song)[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);
            var kickLane = difficulty.Phrases.Single(phrase => phrase.Type == PhraseType.KickLane);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(kickLane.Tick, Is.EqualTo(TICKS(0)));
                Assert.That(kickLane.TickLength, Is.EqualTo(TICKS(3)));
                Assert.That(difficulty.Phrases.Any(phrase => phrase.Type == PhraseType.BigRockEnding), Is.True);
                Assert.That(difficulty.Phrases.Any(phrase => phrase.Type == PhraseType.Coda), Is.True);
                Assert.That(difficulty.Notes.All(note => note.IsKickLane), Is.True);
                Assert.That(difficulty.Notes[0].IsKickLaneStart, Is.True);
                Assert.That(difficulty.Notes[^1].IsKickLaneEnd, Is.True);
                Assert.That(difficulty.Notes.Any(note => note.IsCodaEnd), Is.True);
            }
        }

        [Test]
        public void EliteDownchart_RetainsFlamChildren()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonNote(TICKS(0), (int) EliteDrumNote.EliteDrumPad.Snare, 0, Flags.EliteDrums_Flam));
            chart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Kick));

            var difficulty = LoadDowncharts(song)[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(difficulty.Notes, Has.Count.EqualTo(2));
                Assert.That(CountChildren(difficulty.Notes[0]), Is.EqualTo(2));
                Assert.That(difficulty.Notes[0].Pad, Is.EqualTo((int) FourLaneDrumPad.RedDrum));
                Assert.That(difficulty.Notes[1].Pad, Is.EqualTo((int) FourLaneDrumPad.Kick));
            }
        }

        [Test]
        public void EliteDrumsDownchart_TransfersOriginsPerDifficultyAndFlamChild()
        {
            var song = CreateSong();
            var medium = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Medium);
            medium.Add(new MoonNote(TICKS(0), (int) EliteDrumNote.EliteDrumPad.Snare, 0, Flags.EliteDrums_Flam));
            var expert = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            expert.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Kick));

            var generated = LoadDowncharts(song)[Instrument.ProDrums];
            var mediumPhysical = ExpandNotes(generated.GetDifficulty(Difficulty.Medium).Notes).ToArray();
            var expertNotes = generated.GetDifficulty(Difficulty.Expert).Notes;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mediumPhysical, Has.Length.EqualTo(2));
                Assert.That(mediumPhysical.All(note => note.ConversionOrigin is not null), Is.True);
                Assert.That(mediumPhysical.Select(note => note.ConversionOrigin!.ExpansionOrdinal),
                    Is.EqualTo(new[] { 0, 1 }));
                Assert.That(expertNotes, Has.Count.EqualTo(1));
                Assert.That(expertNotes[0].ConversionOrigin, Is.Not.Null);
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
        public void EliteDrumsDownchart_UnforcedPadsUseFourLaneIntermediateForAllTargets()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonNote(TICKS(0), (int) EliteDrumNote.EliteDrumPad.Ride));
            chart.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Tom2));
            chart.Add(new MoonNote(TICKS(2), (int) EliteDrumNote.EliteDrumPad.RightCrash));
            chart.Add(new MoonNote(TICKS(3), (int) EliteDrumNote.EliteDrumPad.Tom3));

            var downcharts = LoadDowncharts(song);
            var pro = downcharts[Instrument.ProDrums].GetDifficulty(Difficulty.Expert).Notes;
            var five = downcharts[Instrument.FiveLaneDrums].GetDifficulty(Difficulty.Expert).Notes;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(pro.Select(note => note.Pad).ToArray(), Is.EqualTo(new[]
                {
                    (int) FourLaneDrumPad.BlueCymbal, (int) FourLaneDrumPad.BlueDrum,
                    (int) FourLaneDrumPad.GreenCymbal, (int) FourLaneDrumPad.GreenDrum
                }));
                Assert.That(five.Select(note => note.Pad).ToArray(), Is.EqualTo(new[]
                {
                    (int) FiveLaneDrumPad.Orange, (int) FiveLaneDrumPad.Blue,
                    (int) FiveLaneDrumPad.Orange, (int) FiveLaneDrumPad.Green
                }));
            }
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

        [Test]
        public void EliteDrumsDownchart_SameTickTomCymbalCollisionRetainsSerialConversion()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);

            // Tom2 and Ride both initially map to blue, so this is an actual same-tick
            // tom/cymbal collision rather than a suppressed pedal chord. The resolver
            // nudges the tom left before each requested target performs its conversion.
            var tom = new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Tom2);
            var ride = new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Ride);
            chart.notes.Add(tom);
            chart.notes.Add(ride);
            tom.next = ride;
            ride.previous = tom;
            Assert.That(chart.notes, Has.Count.EqualTo(2));

            var downcharts = LoadDowncharts(song);
            var pro = downcharts[Instrument.ProDrums].GetDifficulty(Difficulty.Expert).Notes;
            var four = downcharts[Instrument.FourLaneDrums].GetDifficulty(Difficulty.Expert).Notes;
            var five = downcharts[Instrument.FiveLaneDrums].GetDifficulty(Difficulty.Expert).Notes;
            var proNotes = ExpandNotes(pro).ToArray();
            var fourNotes = ExpandNotes(four).ToArray();
            var fiveNotes = ExpandNotes(five).ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(proNotes.Select(note => note.Tick).ToArray(), Is.EqualTo(new[] { TICKS(1), TICKS(1) }));
                Assert.That(fourNotes.Select(note => note.Tick).ToArray(), Is.EqualTo(new[] { TICKS(1), TICKS(1) }));
                Assert.That(fiveNotes.Select(note => note.Tick).ToArray(), Is.EqualTo(new[] { TICKS(1), TICKS(1) }));

                // The resolved Four Lane pads are the retained serial intermediate:
                // the tom is nudged to yellow and the cymbal remains blue.
                Assert.That(proNotes.Select(note => note.Pad).ToArray(), Is.EqualTo(new[]
                {
                    (int)FourLaneDrumPad.YellowDrum, (int)FourLaneDrumPad.BlueCymbal
                }));
                Assert.That(fourNotes.Select(note => note.Pad).ToArray(), Is.EqualTo(new[]
                {
                    (int)FourLaneDrumPad.YellowDrum, (int)FourLaneDrumPad.BlueDrum
                }));
                Assert.That(fiveNotes.Select(note => note.Pad).ToArray(), Is.EqualTo(new[]
                {
                    (int)FiveLaneDrumPad.Blue, (int)FiveLaneDrumPad.Orange
                }));
            }
        }

        [Test]
        public void ForcedEliteDrumsDownchart_DiscoFlipAppliesOnceToProButNotFourLane()
        {
            var song = CreateSong();
            var chart = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            chart.Add(new MoonPhrase(TICKS(1), TICKS(2), MoonPhrase.Type.EliteDrums_DiscoFlip));
            chart.Add(new MoonNote(TICKS(0), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(1), (int)EliteDrumNote.EliteDrumPad.Snare));
            chart.Add(new MoonNote(TICKS(2), (int)EliteDrumNote.EliteDrumPad.HiHat));
            chart.Add(new MoonNote(TICKS(3), (int)EliteDrumNote.EliteDrumPad.Snare));

            var downcharts = LoadDowncharts(song);
            var pro = downcharts[Instrument.ProDrums].GetDifficulty(Difficulty.Expert);
            var four = downcharts[Instrument.FourLaneDrums].GetDifficulty(Difficulty.Expert);
            var five = downcharts[Instrument.FiveLaneDrums].GetDifficulty(Difficulty.Expert);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(pro.Notes.Select(n => n.Pad).ToArray(), Is.EqualTo(new[] { (int)FourLaneDrumPad.RedDrum, (int)FourLaneDrumPad.YellowCymbal, (int)FourLaneDrumPad.RedDrum, (int)FourLaneDrumPad.RedDrum }));
                Assert.That(four.Notes.Select(n => n.Pad).ToArray(), Is.EqualTo(new[] { (int)FourLaneDrumPad.RedDrum, (int)FourLaneDrumPad.RedDrum, (int)FourLaneDrumPad.YellowDrum, (int)FourLaneDrumPad.RedDrum }));
                Assert.That(five.Notes.Select(n => n.Pad).ToArray(), Is.EqualTo(new[] { (int)FiveLaneDrumPad.Red, (int)FiveLaneDrumPad.Yellow, (int)FiveLaneDrumPad.Red, (int)FiveLaneDrumPad.Red }));
                Assert.That(pro.Notes.Select(n => n.Stem).ToArray(), Is.EqualTo(new[] { DrumStem.Snare, DrumStem.Else, DrumStem.Snare, DrumStem.Snare }));
                Assert.That(four.Notes.Select(n => n.Stem).ToArray(), Is.EqualTo(new[] { DrumStem.Snare, DrumStem.Else, DrumStem.Snare, DrumStem.Snare }));
                Assert.That(five.Notes.Select(n => n.Stem).ToArray(), Is.EqualTo(new[] { DrumStem.Snare, DrumStem.Else, DrumStem.Snare, DrumStem.Snare }));
                Assert.That(pro.Phrases.Any(p => p.Type == PhraseType.TrillLane), Is.False);
                Assert.That(four.Phrases.Any(p => p.Type == PhraseType.TrillLane), Is.False);
                Assert.That(five.Phrases.Any(p => p.Type == PhraseType.TrillLane), Is.False);
                Assert.That(five.Notes[0].IsLane, Is.False);
                Assert.That(five.Notes[1].IsLane, Is.False);
                Assert.That(five.Notes[2].IsLane, Is.False);
                Assert.That(five.Notes[3].IsLane, Is.False);
                Assert.That((pro.Notes[0].DrumFlags & DrumNoteFlags.KickLane) == 0, Is.True);
                Assert.That((four.Notes[0].DrumFlags & DrumNoteFlags.KickLane) == 0, Is.True);
            }
        }

        [Test]
        public void ForcedEliteDrumsDownchart_TextEventsAreIsolatedPerDifficultyAndTarget()
        {
            var song = CreateSong();
            var medium = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Medium);
            medium.Add(new MoonPhrase(TICKS(2), TICKS(2), MoonPhrase.Type.EliteDrums_DiscoFlip));
            medium.Add(new MoonNote(TICKS(2), (int)EliteDrumNote.EliteDrumPad.Snare));
            var expert = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            expert.Add(new MoonPhrase(TICKS(4), TICKS(2), MoonPhrase.Type.EliteDrums_DiscoFlip));
            expert.Add(new MoonNote(TICKS(4), (int)EliteDrumNote.EliteDrumPad.Snare));

            var downcharts = LoadDowncharts(song);
            foreach (var target in new[] { Instrument.ProDrums, Instrument.FourLaneDrums, Instrument.FiveLaneDrums })
            {
                var track = downcharts[target];
                var mediumEvents = track.GetDifficulty(Difficulty.Medium).TextEvents;
                var expertEvents = track.GetDifficulty(Difficulty.Expert).TextEvents;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(mediumEvents.Select(e => e.Text), Is.EqualTo(new[] { "mix 1 drums0", "mix 1 drums0d", "mix 1 drums0" }));
                    Assert.That(mediumEvents.Select(e => e.Tick).ToArray(), Is.EqualTo(new[] { TICKS(0), TICKS(2), TICKS(4) }));
                    Assert.That(expertEvents.Select(e => e.Text), Is.EqualTo(new[] { "mix 3 drums0", "mix 3 drums0d", "mix 3 drums0" }));
                    Assert.That(expertEvents.Select(e => e.Tick).ToArray(), Is.EqualTo(new[] { TICKS(0), TICKS(4), TICKS(6) }));
                }
            }
        }

        [Test]
        public void EliteDrumsDownchart_GeneratedThenNativeFiveLaneKeepsNativeMode()
        {
            var song = CreateSong();
            var native = song.GetChart(MoonSong.MoonInstrument.Drums, MoonSong.Difficulty.Expert);
            native.Add(new MoonNote(TICKS(0), (int) DrumPad.Blue, 0, Flags.ProDrums_Cymbal));
            var elite = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            elite.Add(new MoonNote(TICKS(1), (int) EliteDrumNote.EliteDrumPad.Ride));

            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FiveLane;
            settings.EliteDrumsDownchartOutputs = new[] { Instrument.FiveLaneDrums };
            var loader = new MoonSongLoader(song, settings);
            var eliteTrack = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);

            var generated = loader.LoadEliteDrumsDownchartTracks(eliteTrack)[Instrument.FiveLaneDrums];
            var direct = loader.LoadDrumsTrack(Instrument.FiveLaneDrums, eliteTrack);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(generated.GetDifficulty(Difficulty.Expert).Notes[0].Pad, Is.EqualTo((int) FiveLaneDrumPad.Orange));
                Assert.That(direct.GetDifficulty(Difficulty.Expert).Notes[0].Pad, Is.EqualTo((int) FiveLaneDrumPad.Blue));
            }
        }

        private static IEnumerable<DrumNote> ExpandNotes(IEnumerable<DrumNote> notes)
        {
            foreach (var note in notes)
            {
                foreach (var expanded in note.AllNotes)
                {
                    yield return expanded;
                }
            }
        }

        private static int CountChildren(DrumNote note)
        {
            var count = 0;
            foreach (var child in note.AllNotes)
            {
                count++;
            }

            return count;
        }

        private static IReadOnlyDictionary<Instrument, InstrumentTrack<DrumNote>> LoadDowncharts(MoonSong song)
        {
            var eliteTrack = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);

            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            settings.EliteDrumsDownchartOutputs = new[]
            {
                Instrument.ProDrums, Instrument.FourLaneDrums, Instrument.FiveLaneDrums
            };
            return new MoonSongLoader(song, settings).LoadEliteDrumsDownchartTracks(eliteTrack);
        }
    }
}
