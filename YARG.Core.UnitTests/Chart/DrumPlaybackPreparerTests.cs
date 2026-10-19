using System;
using System.IO;
using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Parsing;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Chart
{
    public class DrumPlaybackPreparerTests
    {
        private static SongChart Source(DrumSourceFormat format, bool extraOnly = false, Difficulty tier = Difficulty.Easy)
        {
            var song = new MoonSong(192);
            var instrument = format == DrumSourceFormat.Elite ? MoonSong.MoonInstrument.EliteDrums : MoonSong.MoonInstrument.Drums;
            var moonTier = tier switch
            {
                Difficulty.Medium => MoonSong.Difficulty.Medium,
                Difficulty.Hard => MoonSong.Difficulty.Hard,
                Difficulty.Expert => MoonSong.Difficulty.Expert,
                _ => MoonSong.Difficulty.Easy,
            };
            var moon = song.GetChart(instrument, moonTier);
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
            Difficulty tier = Difficulty.Easy) => new(output, format, tier, tier == Difficulty.Beginner ? Difficulty.Easy : tier, policy,
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

        private static int EngineNoteScore(PreparedDrumPlayback prepared, SongChart chart)
        {
            var parameters = EnginePreset.Default.Drums.Create(new float[] { 1, 2, 3, 4, 5, 6 },
                new float[] { 1, 2, 3, 4, 5, 6 }, DrumsEngineParameters.DrumMode.ProFourLane);
            return prepared.Elite is { } elite
                ? new EliteDrumsEngine(elite, chart.SyncTrack, parameters, true, true).BaseNoteScore
                : new YargDrumsEngine(prepared.Classic!, chart.SyncTrack, parameters, true, true).BaseNoteScore;
        }

        [Test]
        public void ScoreIdentity_ContentMatrixMatchesRealEngineScores()
        {
            foreach (var format in Enum.GetValues<DrumSourceFormat>())
            foreach (var output in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums, Instrument.EliteDrums })
            foreach (var tier in new[] { Difficulty.Beginner, Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert })
            foreach (string content in new[] { "ordinary", "mixed", "extraOnly", "extraWithHand", "paired", "standaloneFlam", "coda", "control" })
            {
                var sourceTier = tier == Difficulty.Beginner ? Difficulty.Easy : tier;
                var chart = Source(format, tier: sourceTier);
                var source = chart.AuthoredDrumSources.Tiers[(format, sourceTier)];
                var classic = format == DrumSourceFormat.Elite ? null : source.CloneClassicDifficulty();
                var elite = format == DrumSourceFormat.Elite ? source.CloneEliteDifficulty() : null;
                var paired = Array.Empty<uint>();
                bool ordinary = content != "extraOnly" && content != "extraWithHand";
                bool extra = content != "ordinary" && content != "standaloneFlam";
                bool other = content == "mixed" || content == "extraWithHand";
                if (classic is not null)
                {
                    classic.Phrases.Clear();
                    if (!other) classic.Notes.RemoveAt(2);
                    if (!extra) classic.Notes.RemoveAt(1);
                    if (!ordinary) classic.Notes.RemoveAt(0);
                    if (content == "extraWithHand")
                    {
                        var hand = classic.Notes[1].CloneWithoutChildNotes();
                        hand.Tick = classic.Notes[0].Tick;
                        hand.Time = classic.Notes[0].Time;
                        classic.Notes[0].AddChildNote(hand);
                        classic.Notes.RemoveAt(1);
                    }
                    if (content == "coda") classic.Phrases.Add(new Phrase(PhraseType.BigRockEnding, 0, 5, 0, 1000));
                }
                else
                {
                    elite!.Phrases.Clear();
                    if (!other) elite.Notes.RemoveAt(2);
                    if (!extra) elite.Notes.RemoveAt(1);
                    if (!ordinary) elite.Notes.RemoveAt(0);
                    if (content == "extraWithHand")
                    {
                        var hand = elite.Notes[1].CloneWithoutChildNotes();
                        hand.Tick = elite.Notes[0].Tick;
                        hand.Time = elite.Notes[0].Time;
                        elite.Notes[0].AddChildNote(hand);
                        elite.Notes.RemoveAt(1);
                    }
                    if (content == "standaloneFlam") elite.Notes[0].IsFlam = true;
                    if (content == "paired")
                    {
                        elite.Notes.RemoveAt(1);
                        elite.Notes[0].IsFlam = true;
                        paired = new uint[] { 0 };
                    }
                    if (content == "coda") elite.Phrases.Add(new Phrase(PhraseType.BigRockEnding, 0, 5, 0, 1000));
                    if (content == "control")
                        foreach (var note in elite.Notes) note.HatPedalType = EliteDrumsHatPedalType.InvisibleTerminator;
                }
                var facts = new DrumSourceTierFacts(format, sourceTier, ordinary, extra, other,
                    content == "paired" && format == DrumSourceFormat.Elite, true);
                var collection = new AuthoredDrumSourceCollection();
                collection.Add(new AuthoredDrumSourceTier(facts, classic, elite, paired));
                chart.AuthoredDrumSources = collection;
                var enabledState = State(output, format, DrumExtraKickPolicy.Include, tier);
                var enabled = DrumPlaybackPreparer.PrepareForScore(chart, enabledState);
                var disabled = DrumPlaybackPreparer.Prepare(chart, State(output, format, facts.KickPolicy(false), tier));
                int enabledScore = EngineNoteScore(enabled, chart);
                int disabledScore = EngineNoteScore(disabled, chart);
                string context = $"{format}/{output}/{tier}/{content}";
                Assert.That(enabled.ScoreIdentity!.Value.HasExtraKickContribution,
                    Is.EqualTo(tier != Difficulty.Beginner && enabledScore > disabledScore), context);
                Assert.That(DrumPlaybackPreparer.IsPlayable(enabled), Is.EqualTo(enabledScore > 0), context);
                Assert.That(enabled.ScoreIdentity.Value.BaseDifficulty, Is.EqualTo(tier), context);
                Assert.That(enabledState.EffectiveExtraContent, Is.True, context);
                var repeat = DrumPlaybackPreparer.PrepareForScore(chart, enabledState);
                Assert.That(repeat.ScoreIdentity, Is.EqualTo(enabled.ScoreIdentity), context);
                var disabledForScore = DrumPlaybackPreparer.PrepareForScore(chart, State(output, format, facts.KickPolicy(false), tier));
                Assert.That(disabledForScore.ScoreIdentity!.Value.HasExtraKickContribution, Is.False, context);
                if (content == "paired" && format == DrumSourceFormat.Elite && output == Instrument.EliteDrums)
                    Assert.That(enabled.ScoreIdentity.Value.HasExtraKickContribution, Is.False, context);
                if (content == "coda")
                {
                    Assert.That(enabled.ScoreIdentity.Value.HasExtraKickContribution, Is.False, context);
                    Assert.That(DrumPlaybackPreparer.IsPlayable(enabled), Is.False, context);
                }
                if (content == "extraOnly") Assert.That(enabled.ScoreIdentity.Value.HasExtraKickContribution, Is.False, context);
                if (content == "extraWithHand" && tier == Difficulty.Beginner) Assert.That(enabled.ScoreIdentity.Value.HasExtraKickContribution, Is.False, context);
            }
        }

        [Test]
        public void ScoreIdentity_PureNoKicksTransformAppliesToBothVariants()
        {
            foreach (var format in Enum.GetValues<DrumSourceFormat>())
            foreach (var output in new[] { Instrument.ProDrums, Instrument.EliteDrums })
            {
                var chart = Source(format);
                var state = State(output, format, DrumExtraKickPolicy.Include);
                var prepared = DrumPlaybackPreparer.PrepareForScore(chart, state,
                    n => n.RemoveKickDrumNotes(), n => n.RemoveEliteKickDrumNotes());
                Assert.That(prepared.ScoreIdentity!.Value.HasExtraKickContribution, Is.False);
                Assert.That(EngineNoteScore(prepared, chart), Is.GreaterThan(0));
                Assert.That(DrumPlaybackPreparer.PrepareForScore(chart, state).ScoreIdentity!.Value.HasExtraKickContribution, Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ScoreIdentity_BeginnerWildcardDoesNotRequireKickEvenWhenTotalScoreGrows(bool sameTick)
        {
            foreach (var format in Enum.GetValues<DrumSourceFormat>())
            foreach (var output in new[] { Instrument.FourLaneDrums, Instrument.ProDrums, Instrument.FiveLaneDrums, Instrument.EliteDrums })
            {
                var chart = Source(format);
                var source = chart.AuthoredDrumSources.Tiers[(format, Difficulty.Easy)];
                var classic = format == DrumSourceFormat.Elite ? null : source.CloneClassicDifficulty();
                var elite = format == DrumSourceFormat.Elite ? source.CloneEliteDifficulty() : null;
                if (classic is not null)
                {
                    classic.Phrases.Clear();
                    if (sameTick)
                    {
                        var hand = classic.Notes[2];
                        hand.Tick = classic.Notes[1].Tick;
                        hand.Time = classic.Notes[1].Time;
                        classic.Notes[1].AddChildNote(hand);
                        classic.Notes.RemoveAt(2);
                    }
                }
                else
                {
                    elite!.Phrases.Clear();
                    if (sameTick)
                    {
                        var hand = elite.Notes[2];
                        hand.Tick = elite.Notes[1].Tick;
                        hand.Time = elite.Notes[1].Time;
                        elite.Notes[1].AddChildNote(hand);
                        elite.Notes.RemoveAt(2);
                    }
                }
                var collection = new AuthoredDrumSourceCollection();
                collection.Add(new AuthoredDrumSourceTier(new DrumSourceTierFacts(format, Difficulty.Easy,
                    true, true, true, false, true), classic, elite, Array.Empty<uint>()));
                chart.AuthoredDrumSources = collection;
                var enabled = DrumPlaybackPreparer.PrepareForScore(chart,
                    State(output, format, DrumExtraKickPolicy.Include, Difficulty.Beginner));
                var disabled = DrumPlaybackPreparer.PrepareForScore(chart,
                    State(output, format, DrumExtraKickPolicy.Remove, Difficulty.Beginner));
                int enabledScore = EngineNoteScore(enabled, chart);
                int disabledScore = EngineNoteScore(disabled, chart);
                string context = $"{format}/{output}/sameTick={sameTick}";
                // An isolated extra kick adds a wildcard score unit, not a kick obligation.
                // A same-tick hand already supplies that wildcard, so even total score is equal.
                Assert.That(enabledScore, sameTick ? Is.EqualTo(disabledScore) : Is.GreaterThan(disabledScore), context);
                Assert.That(enabled.ScoreIdentity!.Value.HasExtraKickContribution, Is.False, context);
                Assert.That(disabled.ScoreIdentity!.Value.HasExtraKickContribution, Is.False, context);
                if (enabled.Elite is { } preparedElite)
                    Assert.That(preparedElite.Notes.All(n => n.Pad == (int)EliteDrumPad.Wildcard && n.IsNeutral), Is.True, context);
                else
                    Assert.That(enabled.Classic!.Notes.All(n => n.Pad == (int)FourLaneDrumPad.Wildcard && n.Type == DrumNoteType.Neutral), Is.True, context);
            }
        }

        [Test]
        public void ScoreIdentity_NonKickScoringGrowthCannotContribute()
        {
            var chart = Source(DrumSourceFormat.Elite);
            var state = State(Instrument.EliteDrums, DrumSourceFormat.Elite, DrumExtraKickPolicy.Include);
            static void Transform(InstrumentDifficulty<EliteDrumNote> notes)
            {
                bool hadExtra = notes.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Any(n => n.IsDoubleKick);
                notes.RemoveEliteKickDrumNotes();
                foreach (var note in notes.Notes) note.IsFlam = hadExtra;
            }
            var prepared = DrumPlaybackPreparer.PrepareForScore(chart, state, pureEliteModifiers: Transform);
            var baseline = DrumPlaybackPreparer.Prepare(chart,
                State(Instrument.EliteDrums, DrumSourceFormat.Elite, DrumExtraKickPolicy.Remove), eliteModifiers: Transform);
            Assert.That(EngineNoteScore(prepared, chart), Is.GreaterThan(EngineNoteScore(baseline, chart)));
            Assert.That(prepared.ScoreIdentity!.Value.HasExtraKickContribution, Is.False);
        }

        [Test]
        public void ScoreIdentity_ValueEqualityIncludesBaseTierContributionAndCategory()
        {
            var identity = new DrumScoreIdentity(Difficulty.Easy, true, DrumScoreCategory.NativeElite);
            Assert.That(identity, Is.EqualTo(new DrumScoreIdentity(Difficulty.Easy, true, DrumScoreCategory.NativeElite)));
            Assert.That(identity.GetHashCode(), Is.EqualTo(new DrumScoreIdentity(Difficulty.Easy, true, DrumScoreCategory.NativeElite).GetHashCode()));
            Assert.That(identity, Is.Not.EqualTo(new DrumScoreIdentity(Difficulty.Beginner, true, DrumScoreCategory.NativeElite)));
            Assert.That(identity, Is.Not.EqualTo(new DrumScoreIdentity(Difficulty.Easy, false, DrumScoreCategory.NativeElite)));
            Assert.That(identity, Is.Not.EqualTo(new DrumScoreIdentity(Difficulty.Easy, true, DrumScoreCategory.FourLaneDerivedElite)));
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
