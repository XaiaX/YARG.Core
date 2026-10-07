using System;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Chart
{
    public sealed class EliteDrumsUpconverterTests
    {
        private static DrumNote Gem(FourLaneDrumPad pad, uint tick, bool doubleKick = false)
            => new(pad, DrumNoteType.Accent, DrumNoteFlags.StarPowerActivator,
                NoteFlags.StarPower | NoteFlags.Solo | NoteFlags.BigRockEnding, tick / 100.0, tick, doubleKick);

        [TestCase(Instrument.ProDrums)]
        [TestCase(Instrument.FourLaneDrums)]
        public void MapsEveryPadDirectlyAndPreservesTimingDynamicsAndFlags(Instrument instrument)
        {
            var source = new InstrumentDifficulty<DrumNote>(instrument, Difficulty.ExpertPlus);
            var pads = new[] { FourLaneDrumPad.Kick, FourLaneDrumPad.RedDrum, FourLaneDrumPad.YellowCymbal,
                FourLaneDrumPad.BlueCymbal, FourLaneDrumPad.GreenCymbal, FourLaneDrumPad.YellowDrum,
                FourLaneDrumPad.BlueDrum, FourLaneDrumPad.GreenDrum };
            var expected = new[] { EliteDrumPad.Kick, EliteDrumPad.Snare, EliteDrumPad.HiHat,
                EliteDrumPad.Ride, EliteDrumPad.RightCrash, EliteDrumPad.Tom1, EliteDrumPad.Tom2, EliteDrumPad.Tom3 };
            for (int index = 0; index < pads.Length; index++) source.Notes.Add(Gem(pads[index], (uint)index * 100));
            source.Notes.Add(Gem(FourLaneDrumPad.Kick, 800, true));
            source.Notes[1].Type = DrumNoteType.Ghost;

            var result = source.ConvertToEliteDrums();
            Assert.That(result.Instrument, Is.EqualTo(Instrument.EliteDrums));
            Assert.That(result.Difficulty, Is.EqualTo(Difficulty.ExpertPlus));
            Assert.That(result.Notes.Take(8).Select(note => (EliteDrumPad)note.Pad), Is.EqualTo(expected));
            Assert.That(result.Notes[0].IsDoubleKick, Is.False);
            Assert.That(result.Notes[8].IsDoubleKick, Is.True);
            for (int index = 0; index < result.Notes.Count; index++)
            {
                var note = result.Notes[index];
                Assert.That(note.Time, Is.EqualTo(source.Notes[index].Time));
                Assert.That(note.Tick, Is.EqualTo(source.Notes[index].Tick));
                Assert.That(note.Dynamics, Is.EqualTo(source.Notes[index].Type));
                Assert.That(note.Flags, Is.EqualTo(source.Notes[index].Flags));
                Assert.That(note.IsStarPowerActivator, Is.True);
                Assert.That(note.HatState, Is.EqualTo(EliteDrumsHatState.Indifferent));
                Assert.That(note.IsFlam, Is.False);
                Assert.That(note.SourceDefinition!.PhysicalOrdinal, Is.EqualTo(index));
                Assert.That(note.PreviousNote, Is.SameAs(index == 0 ? null : result.Notes[index - 1]));
            }
            Assert.That(result.Notes[0].NextNote, Is.SameAs(result.Notes[1]));
            Assert.That(source.Notes[0].NextNote, Is.Null);
            Assert.That(result.EliteDrumVisualDescriptors, Is.Empty);
        }

        [Test]
        public void PreservesChordMembersAndClonesAnalogousEventsWithoutMutatingSource()
        {
            var source = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Hard);
            var chord = Gem(FourLaneDrumPad.Kick, 100);
            chord.AddChildNote(Gem(FourLaneDrumPad.YellowCymbal, 100));
            chord.AddChildNote(Gem(FourLaneDrumPad.BlueDrum, 100));
            source.Notes.Add(chord);
            foreach (var type in new[] { PhraseType.StarPower, PhraseType.Solo, PhraseType.BigRockEnding,
                PhraseType.Coda, PhraseType.DrumFill })
                source.Phrases.Add(new Phrase(type, 1, 2, 100, 200));
            var result = source.ConvertToEliteDrums();
            Assert.That(result.Notes.Single().ChildNotes.Prepend(result.Notes.Single()).Select(note => (EliteDrumPad)note.Pad),
                Is.EqualTo(new[] { EliteDrumPad.Kick, EliteDrumPad.HiHat, EliteDrumPad.Tom2 }));
            Assert.That(result.Phrases.Select(phrase => phrase.Type), Is.EqualTo(source.Phrases.Select(phrase => phrase.Type)));
            for (int index = 0; index < result.Phrases.Count; index++)
            {
                Assert.That(result.Phrases[index], Is.Not.SameAs(source.Phrases[index]));
                Assert.That(result.Phrases[index].TickLength, Is.EqualTo(200));
                Assert.That(result.Phrases[index].TimeLength, Is.EqualTo(2));
            }
            result.Notes[0].ChildNotes[0].Dynamics = DrumNoteType.Ghost;
            Assert.That(chord.ChildNotes[0].Type, Is.EqualTo(DrumNoteType.Accent));
        }

        [Test]
        public void ConvertsRollsUsingActualMemberTypesAndHalfOpenIntervals()
        {
            var source = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
            source.Notes.Add(Gem(FourLaneDrumPad.YellowCymbal, 100));
            source.Notes.Add(Gem(FourLaneDrumPad.BlueDrum, 150));
            source.Notes.Add(Gem(FourLaneDrumPad.Kick, 175, true));
            source.Notes.Add(Gem(FourLaneDrumPad.GreenCymbal, 200));
            source.Notes.Add(Gem(FourLaneDrumPad.YellowDrum, 250));
            source.Notes[0].ActivateFlag(NoteFlags.Trill);
            source.Notes[1].ActivateFlag(NoteFlags.Trill);
            source.Notes[2].ActivateFlag(DrumNoteFlags.KickLane);
            source.Notes[3].ActivateFlag(NoteFlags.Tremolo);
            source.Notes[4].ActivateFlag(NoteFlags.Tremolo);
            source.Phrases.Add(new Phrase(PhraseType.TrillLane, 1, 1, 100, 100));
            source.Phrases.Add(new Phrase(PhraseType.KickLane, 1, 1, 100, 100));
            source.Phrases.Add(new Phrase(PhraseType.TremoloLane, 2, 1, 200, 100));
            source.Phrases.Add(new Phrase(PhraseType.TrillLane, 3, 1, 300, 100));
            var result = source.ConvertToEliteDrums();
            Assert.That(result.Phrases.Select(phrase => phrase.Type), Is.EquivalentTo(new[] {
                PhraseType.EliteDrums_HiHatLane, PhraseType.EliteDrums_Tom2Lane, PhraseType.EliteDrums_KickLane,
                PhraseType.EliteDrums_RightCrashLane, PhraseType.EliteDrums_Tom1Lane }));
            Assert.That(result.EliteDrumNativeAuthoredLaneRecords.Count, Is.EqualTo(5));
            foreach (var record in result.EliteDrumNativeAuthoredLaneRecords)
            {
                Assert.That(record.MemberSources.Count, Is.EqualTo(1));
                Assert.That(record.MemberSources.Single().Pad, Is.EqualTo((int)record.AuthoredPad));
                Assert.That(record.MemberSources.Single().StartTick, Is.InRange(record.StartTick, record.EndTick - 1));
                Assert.That(result.Phrases[record.PhraseOrdinal].Type, Is.EqualTo(record.LaneType));
                Assert.That(result.Notes.SelectMany(note => note.ChildNotes.Prepend(note))
                    .Any(note => ReferenceEquals(note.SourceDefinition, record.MemberSources.Single())), Is.True);
            }
            Assert.That(source.Phrases.Count, Is.EqualTo(4));
        }

        [Test]
        public void DoesNotInventSecondTrillLaneOrKickMembership()
        {
            var source = new InstrumentDifficulty<DrumNote>(Instrument.FourLaneDrums, Difficulty.Easy);
            source.Notes.Add(Gem(FourLaneDrumPad.RedDrum, 100));
            source.Notes[0].ActivateFlag(NoteFlags.Trill);
            source.Phrases.Add(new Phrase(PhraseType.TrillLane, 1, 1, 100, 100));
            source.Phrases.Add(new Phrase(PhraseType.KickLane, 1, 1, 100, 100));
            var result = source.ConvertToEliteDrums();
            Assert.That(result.Phrases.Single().Type, Is.EqualTo(PhraseType.EliteDrums_SnareLane));
            Assert.That(result.EliteDrumNativeAuthoredLaneRecords.Single().MemberSources.Count, Is.EqualTo(1));
        }

        [TestCase(PhraseType.TremoloLane, NoteFlags.Tremolo)]
        [TestCase(PhraseType.TrillLane, NoteFlags.Trill)]
        public void ExcludesUnrelatedHiHatStrikesFromValidatedHandLanes(PhraseType type, NoteFlags flag)
        {
            var source = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
            for (uint tick = 100; tick < 200; tick += 25)
            {
                var snare = Gem(FourLaneDrumPad.RedDrum, tick);
                snare.ActivateFlag(flag);
                snare.AddChildNote(Gem(FourLaneDrumPad.YellowCymbal, tick));
                if (type == PhraseType.TrillLane)
                {
                    var tom = Gem(FourLaneDrumPad.BlueDrum, tick);
                    tom.ActivateFlag(flag);
                    snare.AddChildNote(tom);
                }
                source.Notes.Add(snare);
            }
            source.Phrases.Add(new Phrase(type, 1, 1, 100, 100));
            var result = source.ConvertToEliteDrums();
            Assert.That(result.Phrases.Select(phrase => phrase.Type), Is.EquivalentTo(type == PhraseType.TrillLane
                ? new[] { PhraseType.EliteDrums_SnareLane, PhraseType.EliteDrums_Tom2Lane }
                : new[] { PhraseType.EliteDrums_SnareLane }));
            Assert.That(result.EliteDrumNativeAuthoredLaneRecords.All(record => record.MemberSources.Count == 4), Is.True);
            Assert.That(result.EliteDrumNativeAuthoredLaneRecords.Any(record => record.AuthoredPad == EliteDrumPad.HiHat), Is.False);
        }

        [Test]
        public void UnvalidatedRollDoesNotCreateSpeculativeLaneRecords()
        {
            var source = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
            source.Notes.Add(Gem(FourLaneDrumPad.RedDrum, 100));
            source.Phrases.Add(new Phrase(PhraseType.TremoloLane, 1, 1, 100, 100));
            Assert.That(source.ConvertToEliteDrums().EliteDrumNativeAuthoredLaneRecords, Is.Empty);
        }

        [Test]
        public void RejectsNonNativeOrUnsupportedSources()
        {
            Assert.Throws<ArgumentNullException>(() => EliteDrumsUpconverter.ConvertToEliteDrums(null!));
            Assert.Throws<ArgumentException>(() => new InstrumentDifficulty<DrumNote>(Instrument.FiveFretGuitar,
                Difficulty.Expert).ConvertToEliteDrums());
            var source = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
            source.Notes.Add(Gem(FourLaneDrumPad.Wildcard, 100));
            Assert.Throws<ArgumentException>(() => source.ConvertToEliteDrums());
            source.Notes.Clear();
            source.Notes.Add(new DrumNote(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral, DrumNoteFlags.None,
                NoteFlags.None, 1, 100, conversionOrigin: new EliteDrumConversionOrigin(
                    new EliteDrumSourceDefinition("generated", 0, (int)EliteDrumPad.Snare, 100, 0))));
            Assert.Throws<ArgumentException>(() => source.ConvertToEliteDrums());
        }
    }
}
