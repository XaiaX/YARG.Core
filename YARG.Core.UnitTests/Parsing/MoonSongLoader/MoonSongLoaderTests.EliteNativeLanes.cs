using System.Linq;
using MoonscraperChartEditor.Song;
using NUnit.Framework;
using YARG.Core;
using YARG.Core.Chart;
using YARG.Core.Parsing;
using static YARG.Core.UnitTests.Parsing.MoonSongLoaderTests;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Parsing
{
    public sealed class MoonSongLoaderTests_EliteNativeLanes
    {
        [Test]
        public void NativeHandLaneRecordsKeepPhraseIdentityAndAuthoredSourceMembership()
        {
            var song = CreateSong();
            var expert = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            expert.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_KickLane));
            expert.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_SnareLane));
            expert.Add(new MoonPhrase(TICKS(1), TICKS(2), MoonPhrase.Type.EliteDrums_SnareLane));
            expert.Add(new MoonPhrase(TICKS(3), TICKS(1), MoonPhrase.Type.EliteDrums_SnareLane));
            expert.Add(new MoonNote(TICKS(0), (int) EliteDrumPad.Snare));
            expert.Add(new MoonNote(TICKS(1), (int) EliteDrumPad.Snare));
            expert.Add(new MoonNote(TICKS(2), (int) EliteDrumPad.Snare));
            expert.Add(new MoonNote(TICKS(2), (int) EliteDrumPad.HiHat));
            expert.Add(new MoonNote(TICKS(3), (int) EliteDrumPad.Snare));

            var track = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);
            var chart = track.GetDifficulty(Difficulty.Expert);
            var records = chart.EliteDrumNativeAuthoredLaneRecords;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(records, Has.Count.EqualTo(4));
                Assert.That(records.Select(record => record.PhraseOrdinal).Distinct().Count(), Is.EqualTo(4));
                Assert.That(records.Single(record => record.LaneType == PhraseType.EliteDrums_KickLane).AuthoredPad,
                    Is.EqualTo(EliteDrumPad.Kick));
                var snareRecords = records.Where(record => record.LaneType == PhraseType.EliteDrums_SnareLane).ToArray();
                Assert.That(snareRecords, Has.Length.EqualTo(3));
                Assert.That(snareRecords[0].MemberSources.Select(source => source.StartTick),
                    Is.EquivalentTo(new[] { TICKS(0), TICKS(1), TICKS(2) }));
                Assert.That(snareRecords[1].MemberSources.Select(source => source.StartTick),
                    Is.EquivalentTo(new[] { TICKS(1), TICKS(2) }));
                Assert.That(snareRecords[2].MemberSources.Select(source => source.StartTick),
                    Is.EqualTo(new[] { TICKS(3) }));
                Assert.That(chart.Notes.SelectMany(note => note.ChildNotes.Prepend(note))
                    .Where(note => note.Pad == (int) EliteDrumPad.Snare && note.Tick == TICKS(1))
                    .Single().SourceDefinition, Is.SameAs(snareRecords[0].MemberSources[1]));
                Assert.That(track.GetDifficulty(Difficulty.Easy).EliteDrumNativeAuthoredLaneRecords, Is.Empty);
            }
        }

        [Test]
        public void NativeLaneCloneAndPracticeSlicePreserveOriginalPhraseIdentity()
        {
            var song = CreateSong();
            var expert = song.GetChart(MoonSong.MoonInstrument.EliteDrums, MoonSong.Difficulty.Expert);
            expert.Add(new MoonPhrase(TICKS(0), TICKS(3), MoonPhrase.Type.EliteDrums_Tom1Lane));
            expert.Add(new MoonNote(TICKS(0), (int) EliteDrumPad.Tom1));
            expert.Add(new MoonNote(TICKS(1), (int) EliteDrumPad.Tom1));
            expert.Add(new MoonNote(TICKS(2), (int) EliteDrumPad.Tom1));
            var track = new MoonSongLoader(song, ParseSettings.Default).LoadEliteDrumsTrack(Instrument.EliteDrums);
            var original = track.GetDifficulty(Difficulty.Expert).EliteDrumNativeAuthoredLaneRecords.Single();
            var clone = track.Clone().GetDifficulty(Difficulty.Expert);
            var sliced = clone.SliceEliteDrumNativeAuthoredLaneRecords(TICKS(1), TICKS(2));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(clone.EliteDrumNativeAuthoredLaneRecords.Single(), Is.SameAs(original));
                Assert.That(sliced, Has.Count.EqualTo(1));
                Assert.That(sliced[0].PhraseOrdinal, Is.EqualTo(original.PhraseOrdinal));
                Assert.That(sliced[0].StartTick, Is.EqualTo(TICKS(1)));
                Assert.That(sliced[0].EndTick, Is.EqualTo(TICKS(2)));
                Assert.That(sliced[0].MemberSources.Select(source => source.StartTick),
                    Is.EqualTo(new[] { TICKS(1) }));
                Assert.That(clone.SliceEliteDrumNativeAuthoredLaneRecords(TICKS(3), TICKS(4)), Is.Empty);
                Assert.That(original.MemberSources, Has.Count.EqualTo(3));
            }
        }
    }
}
