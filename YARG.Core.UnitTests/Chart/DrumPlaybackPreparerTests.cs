using System;
using System.IO;
using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Parsing;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Chart
{
    public class DrumPlaybackPreparerTests
    {
        private static SongChart Source(DrumSourceFormat format, bool extraOnly = false)
        {
            var song = new MoonSong(192);
            var instrument = format == DrumSourceFormat.Elite ? MoonSong.MoonInstrument.EliteDrums : MoonSong.MoonInstrument.Drums;
            var moon = song.GetChart(instrument, MoonSong.Difficulty.Easy);
            int kick = format == DrumSourceFormat.Elite ? (int)MoonNote.EliteDrumPad.Kick : (int)MoonNote.DrumPad.Kick;
            int hand = format == DrumSourceFormat.Elite ? (int)MoonNote.EliteDrumPad.Snare : (int)MoonNote.DrumPad.Red;
            if (!extraOnly) moon.Add(new MoonNote(0, kick));
            moon.Add(new MoonNote(100, kick, 0, MoonNote.Flags.InstrumentPlus));
            moon.Add(new MoonNote(200, hand));
            moon.Add(new MoonPhrase(0, 300, MoonPhrase.Type.Starpower));
            moon.Add(new MoonPhrase(0, 300, MoonPhrase.Type.Solo));
            moon.Add(new MoonPhrase(0, 300, MoonPhrase.Type.ProDrums_Activation));
            var settings = ParseSettings.Default;
            settings.DrumsType = format == DrumSourceFormat.FiveLane ? DrumsType.FiveLane : DrumsType.FourLane;
            var loader = new MoonSongLoader(song, settings);
            var chart = new SongChart(192) { AuthoredDrumSources = loader.LoadAuthoredDrumSources() };
            chart.SyncTrack = loader.LoadSyncTrack();
            return chart;
        }

        private static ResolvedDrumPlayback State(Instrument output, DrumSourceFormat format, DrumExtraKickPolicy policy,
            Difficulty tier = Difficulty.Easy) => new(output, format, tier, Difficulty.Easy, policy,
                policy == DrumExtraKickPolicy.Include, ResolvedDrumPlayback.CategoryFor(output, format));

        [Test]
        public void ExactSourceMissing_FailsWithoutPublicTrackFallback()
        {
            Assert.Throws<InvalidDataException>(() => DrumPlaybackPreparer.Prepare(Source(DrumSourceFormat.FourLane),
                State(Instrument.ProDrums, DrumSourceFormat.Elite, DrumExtraKickPolicy.Include)));
        }

        [Test]
        public void AllSourceOutputAndPolicyCombinations_ArePlayerOwnedAndLinked()
        {
            foreach (var format in Enum.GetValues<DrumSourceFormat>())
            foreach (var output in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums, Instrument.EliteDrums })
            foreach (var policy in Enum.GetValues<DrumExtraKickPolicy>())
            {
                var chart = Source(format, policy == DrumExtraKickPolicy.NormalizeExtraOnly);
                var prepared = DrumPlaybackPreparer.Prepare(chart, State(output, format, policy));
                if (prepared.Classic is { } classic)
                {
                    Assert.That(classic.Instrument, Is.EqualTo(output));
                    Assert.That(classic.Notes.Count, Is.EqualTo(policy == DrumExtraKickPolicy.Include ? 3 : 2), $"{format}/{output}/{policy}");
                    Assert.That(classic.Notes[0].IsStarPowerStart && classic.Notes.Last().IsStarPowerEnd, Is.True);
                    Assert.That(classic.Notes.Last().IsSoloEnd, Is.True);
                    Assert.That(classic.Notes.Last().AllNotes.AnyActivator(), Is.True);
                    AssertLinks(classic);
                    classic.Notes.Clear();
                }
                else
                {
                    var elite = prepared.Elite!;
                    Assert.That(elite.Notes.Count, Is.EqualTo(policy == DrumExtraKickPolicy.Include ? 3 : 2));
                    Assert.That(elite.Notes[0].IsStarPowerStart && elite.Notes.Last().IsStarPowerEnd, Is.True);
                    AssertLinks(elite);
                    elite.Notes.Clear();
                }
                Assert.That(DrumPlaybackPreparer.Prepare(chart, State(output, format, policy)).Classic?.Notes.Count ??
                    DrumPlaybackPreparer.Prepare(chart, State(output, format, policy)).Elite!.Notes.Count, Is.GreaterThan(0));
            }
        }

        private static void AssertLinks<T>(InstrumentDifficulty<T> chart) where T : Note<T>
        {
            for (int i = 0; i < chart.Notes.Count; i++)
                foreach (var note in chart.Notes[i].AllNotes)
                {
                    Assert.That(note.PreviousNote, Is.SameAs(i == 0 ? null : chart.Notes[i - 1]));
                    Assert.That(note.NextNote, Is.SameAs(i + 1 == chart.Notes.Count ? null : chart.Notes[i + 1]));
                }
        }

        [Test]
        public void NoKicksRunsAfterNormalize_ThenBeginnerCollapse()
        {
            foreach (var output in new[] { Instrument.ProDrums, Instrument.EliteDrums })
            {
                var prepared = DrumPlaybackPreparer.Prepare(Source(DrumSourceFormat.Elite, true),
                    State(output, DrumSourceFormat.Elite, DrumExtraKickPolicy.NormalizeExtraOnly, Difficulty.Beginner),
                    n => n.RemoveKickDrumNotes(), n => n.RemoveEliteKickDrumNotes());
                Assert.That(prepared.Classic?.Notes.Count ?? prepared.Elite!.Notes.Count, Is.EqualTo(1));
                if (prepared.Elite is { } elite) Assert.That(elite.Notes[0].Pad, Is.EqualTo((int)EliteDrumPad.Wildcard));
                else Assert.That(prepared.Classic!.Notes[0].Pad, Is.EqualTo((int)FourLaneDrumPad.Wildcard));
            }
        }

        [Test]
        public void ChordFiltering_PreservesHandMembersAndLaneIsolation()
        {
            var source = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Easy);
            for (uint tick = 0; tick < 300; tick += 100)
            {
                var kick = new DrumNote(FourLaneDrumPad.Kick, DrumNoteType.Neutral, DrumNoteFlags.None,
                    NoteFlags.None, tick / 100d, tick, true);
                kick.AddChildNote(new DrumNote(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral, DrumNoteFlags.None,
                    NoteFlags.Tremolo, tick / 100d, tick));
                kick.AddChildNote(new DrumNote(FourLaneDrumPad.BlueDrum, DrumNoteType.Neutral, DrumNoteFlags.None,
                    NoteFlags.None, tick / 100d, tick));
                source.Notes.Add(kick);
            }
            source.Phrases.Add(new Phrase(PhraseType.TremoloLane, 0, 3, 0, 300));
            source.Phrases.Add(new Phrase(PhraseType.BigRockEnding, 0, 3, 0, 200));
            var sources = new AuthoredDrumSourceCollection();
            sources.Add(new AuthoredDrumSourceTier(new DrumSourceTierFacts(DrumSourceFormat.FourLane,
                Difficulty.Easy, false, true, true, false, true), source, null, Array.Empty<uint>()));
            var chart = Source(DrumSourceFormat.FourLane);
            chart.AuthoredDrumSources = sources;
            foreach (var output in new[] { Instrument.ProDrums, Instrument.FiveLaneDrums, Instrument.EliteDrums })
            {
                var result = DrumPlaybackPreparer.Prepare(chart, State(output, DrumSourceFormat.FourLane, DrumExtraKickPolicy.Remove));
                if (result.Classic is { } classic)
                {
                    Assert.That(classic.Notes.Count, Is.EqualTo(3));
                    Assert.That(classic.Notes.All(n => n.ChildNotes.Count == 1), Is.True);
                    Assert.That(classic.Notes.All(n => !n.ChildNotes[0].IsAnyLane), Is.True);
                    Assert.That(classic.Notes.Last().IsBigRockEnding, Is.True);
                    AssertLinks(classic);
                }
                else
                {
                    var elite = result.Elite!;
                    Assert.That(elite.Notes.All(n => n.ChildNotes.Count == 1), Is.True);
                    Assert.That(elite.Notes.All(n => !n.ChildNotes[0].IsAnyLane), Is.True);
                    Assert.That(elite.EliteDrumNativeAuthoredLaneRecords.Single().MemberSources.Count, Is.EqualTo(3));
                    AssertLinks(elite);
                }
            }
        }

        [Test]
        public void PairedKickRemoval_ClearsBothFlamIntentsWithoutLosingOrdinaryKick()
        {
            var chart = Source(DrumSourceFormat.Elite);
            var old = chart.AuthoredDrumSources.Tiers[(DrumSourceFormat.Elite, Difficulty.Easy)].CloneEliteDifficulty();
            var pair = new EliteDrumNote(EliteDrumPad.Kick, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent,
                EliteDrumsHatPedalType.Stomp, true, DrumNoteFlags.None, NoteFlags.None,
                EliteDrumsChannelFlag.None, 0, 0, false, old.Notes[0].SourceDefinition, isAuthoredFlam: true);
            old.Notes[0] = pair;
            var collection = new AuthoredDrumSourceCollection();
            collection.Add(new AuthoredDrumSourceTier(new DrumSourceTierFacts(DrumSourceFormat.Elite,
                Difficulty.Easy, true, true, true, true, true), null, old, new uint[] { 0 }));
            chart.AuthoredDrumSources = collection;
            var prepared = DrumPlaybackPreparer.Prepare(chart, State(Instrument.EliteDrums,
                DrumSourceFormat.Elite, DrumExtraKickPolicy.Remove)).Elite!;
            Assert.That(prepared.Notes.Count, Is.EqualTo(2));
            Assert.That(prepared.Notes[0].Pad, Is.EqualTo((int)EliteDrumPad.Kick));
            Assert.That(prepared.Notes[0].IsFlam || prepared.Notes[0].IsAuthoredFlam, Is.False);
            Assert.That(collection.Tiers[(DrumSourceFormat.Elite, Difficulty.Easy)].CloneEliteDifficulty().Notes[0].IsFlam, Is.True);
        }

        [Test]
        public void NativeFiveMapping_IsDistinctFromFourPro()
        {
            var source = new InstrumentDifficulty<DrumNote>(Instrument.FiveLaneDrums, Difficulty.Easy);
            foreach (var pad in new[] { FiveLaneDrumPad.Red, FiveLaneDrumPad.Yellow, FiveLaneDrumPad.Orange, FiveLaneDrumPad.Blue, FiveLaneDrumPad.Green })
                source.Notes.Add(new DrumNote(pad, DrumNoteType.Neutral, DrumNoteFlags.None, NoteFlags.None, 0, (uint)source.Notes.Count));
            Assert.That(source.ConvertToEliteDrums().Notes.Select(n => (EliteDrumPad)n.Pad),
                Is.EqualTo(new[] { EliteDrumPad.Snare, EliteDrumPad.HiHat, EliteDrumPad.RightCrash, EliteDrumPad.Tom2, EliteDrumPad.Tom3 }));
        }
    }

    internal static class PlaybackTestExtensions
    {
        public static bool AnyActivator(this Note<DrumNote>.AllNotesEnumerator notes)
        {
            foreach (var note in notes) if (note.IsStarPowerActivator) return true;
            return false;
        }
    }
}
