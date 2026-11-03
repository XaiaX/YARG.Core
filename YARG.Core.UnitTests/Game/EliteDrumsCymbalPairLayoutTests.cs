// pattern: Functional Core
using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Game
{
    public sealed class EliteDrumsCymbalPairLayoutTests
    {
        [TestCase("left-crash", "right-crash")]
        [TestCase("left-crash", "ride")]
        [TestCase("ride", "right-crash")]
        public void SupportedPairsCoverBothSourcesAndResolveToTheSharedColumn(string first, string second)
        {
            var original = EliteDrumsLayoutPreset.Compact;
            string id = EliteDrumsLayoutSources.PairId(first, second);
            var paired = EliteDrumsLayoutEditor.Place(original, 8, id);
            Assert.That(EliteDrumsLayoutValidator.Validate(paired).IsRuntimeSupported, Is.True);
            Assert.That(EliteDrumsLayoutEditor.GetUnassigned(paired), Is.Empty);
            Assert.That(paired.Slots[2].Drum, Is.EqualTo("tom1"));
            Assert.That(paired.Merges.Single().EffectiveId, Is.EqualTo(id));
            var resolved = EliteDrumsLayoutResolver.Resolve(paired);
            Assert.That(resolved.SourceToLane[first], Is.EqualTo(resolved.SourceToLane[second]));
            Assert.That(resolved.SavedColumnToLane[8], Is.EqualTo(resolved.SourceToLane[first]));
            Assert.That(original.Merges, Is.Empty);
            Assert.That(original.Slots[2].Cymbal, Is.EqualTo("left-crash"));
        }

        [TestCase("left-crash", "right-crash", "ride")]
        [TestCase("left-crash", "ride", "right-crash")]
        [TestCase("ride", "right-crash", "left-crash")]
        public void ExtractingOneMemberLeavesTheOtherAtItsSavedColumn(string first, string second, string other)
        {
            var paired = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Compact, 8,
                EliteDrumsLayoutSources.PairId(first, second));
            var moved = EliteDrumsLayoutEditor.Place(paired, 9, first);
            Assert.That(moved.Slots[8].Cymbal, Is.EqualTo(second));
            Assert.That(moved.Slots[9].Cymbal, Is.EqualTo(first));
            Assert.That(moved.Merges, Is.Empty);
            Assert.That(EliteDrumsLayoutValidator.Validate(moved).IsRuntimeSupported, Is.True);
            Assert.That(moved.Slots.Any(slot => slot.Cymbal == other), Is.True);
            for (int column = 0; column < 5; column++)
                Assert.That(moved.Slots[column].Drum, Is.EqualTo(paired.Slots[column].Drum));
        }

        [Test]
        public void ChangingPairRetainsUnmovedMemberAndPreservesOtherRowWithoutCompaction()
        {
            var paired = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Compact, 8, "left-crash+ride");
            var replaced = EliteDrumsLayoutEditor.Place(paired, 4, "ride+right-crash");
            Assert.That(replaced.Slots[8].Cymbal, Is.EqualTo("left-crash"));
            Assert.That(replaced.Slots[4].Drum, Is.EqualTo("tom3"));
            Assert.That(replaced.Slots[4].Cymbal, Is.EqualTo("ride+right-crash"));
            Assert.That(replaced.Merges.Select(merge => merge.EffectiveId), Is.EqualTo(new[] { "ride+right-crash" }));
            Assert.That(replaced.Slots[0].Drum, Is.EqualTo("snare"));
            Assert.That(replaced.Slots[7].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
        }

        [Test]
        public void ReplacingDestinationDisplacesItsPreviousRowAndCleansItsPair()
        {
            var paired = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Compact, 2, "left-crash+ride");
            var replaced = EliteDrumsLayoutEditor.Place(paired, 2, "right-crash");
            Assert.That(replaced.Slots[2].Drum, Is.EqualTo("tom1"));
            Assert.That(EliteDrumsLayoutEditor.GetUnassigned(replaced), Is.EqualTo(new[] { "left-crash", "ride" }));
            Assert.That(replaced.Merges, Is.Empty);
            var cleared = EliteDrumsLayoutEditor.ClearRow(paired, 2, false);
            Assert.That(cleared.Slots[2].Drum, Is.EqualTo("tom1"));
            Assert.That(cleared.Merges, Is.Empty);
        }

        [Test]
        public void PairMoveClearAndJsonRoundTripRetainOnlyLayoutData()
        {
            var paired = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Compact, 8, "left-crash+right-crash");
            var moved = EliteDrumsLayoutEditor.MoveColumn(paired, 8, 9);
            Assert.That(moved.Slots[8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
            Assert.That(moved.Slots[9].Cymbal, Is.EqualTo("left-crash+right-crash"));
            string json = JsonConvert.SerializeObject(moved);
            Assert.That(json, Does.Not.Contain("Binding").And.Not.Contain("Device").And.Not.Contain("MIDI"));
            var restored = JsonConvert.DeserializeObject<EliteDrumsLayoutPreset>(json)!;
            Assert.That(EliteDrumsLayoutValidator.Validate(restored).IsRuntimeSupported, Is.True);
            Assert.That(EliteDrumsLayoutResolver.PreviewResolve(restored).SourceToLane["left-crash"],
                Is.EqualTo(EliteDrumsLayoutResolver.PreviewResolve(restored).SourceToLane["right-crash"]));
            Assert.That(EliteDrumsLayoutEditor.Clear(restored, 9).Merges, Is.Empty);
        }

        [TestCase("hi-hat+ride")]
        [TestCase("snare+tom1")]
        [TestCase("left-crash+ride+right-crash")]
        public void EditorNeverCreatesUnsupportedPairOrTriple(string id)
        {
            Assert.Throws<ArgumentException>(() => EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 8, id));
        }

        [Test]
        public void PairDraftPreviewWorksBeforeAllSourcesAreAssigned()
        {
            var paired = EliteDrumsLayoutEditor.Place(new EliteDrumsLayoutPreset(), 7, "left-crash+ride");
            Assert.That(EliteDrumsLayoutValidator.Validate(paired).IsComplete, Is.False);
            Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(paired, out var preview), Is.True);
            Assert.That(preview!.SourceToLane["left-crash"], Is.EqualTo(preview.SourceToLane["ride"]));
            Assert.That(EliteDrumsLayoutResolver.TryResolve(paired, out _), Is.False);
        }
    }
}
