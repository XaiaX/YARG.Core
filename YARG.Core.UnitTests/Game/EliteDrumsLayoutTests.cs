using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Game
{
    public class EliteDrumsLayoutTests
    {
        [Test]
        public void LayoutSchema_DocumentedExampleImport()
        {
            var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            string? path = null;
            while (directory != null)
            {
                string candidate = System.IO.Path.Combine(directory.FullName, "docs", "examples", "elite-drums-layout.json");
                if (System.IO.File.Exists(candidate)) { path = candidate; break; }
                directory = directory.Parent;
            }
            Assert.That(path, Is.Not.Null, "The branch-local documented layout fixture must be available.");
            var preset = JsonConvert.DeserializeObject<EliteDrumsLayoutPreset>(System.IO.File.ReadAllText(path!))!;
            Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsRuntimeSupported, Is.True);
            var resolved = EliteDrumsLayoutResolver.Resolve(preset);
            Assert.That(resolved.Lanes.Count, Is.EqualTo(10));
            Assert.That(resolved.KickMode, Is.EqualTo(EliteDrumsLayoutKickMode.Split));
            Assert.That(resolved.SourceToLane["kick1"], Is.EqualTo(8));
            Assert.That(resolved.SourceToLane["kick2"], Is.EqualTo(9));
        }

        [Test]
        public void DefaultsAreCompleteStableAndDetached()
        {
            var compact = EliteDrumsLayoutPreset.Compact;
            var full = EliteDrumsLayoutPreset.Full;
            Assert.That(compact.Id, Is.EqualTo(new Guid("06871f46-49ea-41ee-a857-a7d0c3022141")));
            Assert.That(full.Id, Is.EqualTo(new Guid("611d5dc0-e3c2-4fd6-8913-693ce1be0a84")));
            Assert.That(EliteDrumsLayoutResolver.Resolve(compact).Lanes.Count, Is.EqualTo(5));
            Assert.That(EliteDrumsLayoutResolver.Resolve(full).Lanes.Count, Is.EqualTo(8));
            Assert.That(full.Slots.Take(8).Select(slot => slot.Drum ?? slot.Cymbal), Is.EqualTo(new[]
                { "snare", "hi-hat", "left-crash", "tom1", "tom2", "tom3", "ride", "right-crash" }));
            Assert.That(compact.Slots[2].Drum, Is.EqualTo("tom1"));
            Assert.That(compact.Slots[2].Cymbal, Is.EqualTo("left-crash"));
            compact.Slots[0].Drum = null;
            Assert.That(EliteDrumsLayoutPreset.Compact.Slots[0].Drum, Is.EqualTo("snare"));
        }

        [Test]
        public void EditorRejectsUnsupportedMergesButAcceptsIncompleteOrdinaryDrafts()
        {
            var preset = EliteDrumsLayoutPreset.Full;
            preset.Merges = new[] { new EliteDrumsLayoutMerge
                { EffectiveId = "tom1+tom2", Sources = new[] { "tom1", "tom2" } } };
            preset.Slots[3].Drum = "tom1+tom2";
            preset.Slots[4] = new EliteDrumsLayoutSlot();
            Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.True);
            Assert.Throws<ArgumentException>(() => EliteDrumsLayoutEditor.Clear(preset, 3));
            Assert.Throws<ArgumentException>(() => EliteDrumsLayoutEditor.Place(preset, 3, "tom1"));
            Assert.Throws<ArgumentException>(() => EliteDrumsLayoutEditor.GetUnassigned(preset));
            var draft = EliteDrumsLayoutEditor.Clear(EliteDrumsLayoutPreset.Full, 3);
            Assert.That(EliteDrumsLayoutValidator.Validate(draft).IsComplete, Is.False);
            Assert.That(EliteDrumsLayoutEditor.GetUnassigned(draft), Does.Contain("tom1"));
            Assert.That(EliteDrumsLayoutValidator.Validate(EliteDrumsLayoutEditor.Place(draft, 3, "tom1"))
                .IsRuntimeSupported, Is.True);
        }

        [Test]
        public void DeclaredMergeRequiresEffectivePairRatherThanIndividualSources()
        {
            var preset = EliteDrumsLayoutPreset.Full;
            preset.Merges = new[] { new EliteDrumsLayoutMerge
            {
                EffectiveId = "tom1+tom2", Sources = new[] { "tom1", "tom2" }
            } };
            var validation = EliteDrumsLayoutValidator.Validate(preset);
            Assert.That(validation.IsStructurallyValid, Is.False);
            Assert.That(validation.IsComplete, Is.False);
            preset.Slots[3].Drum = "tom1+tom2";
            preset.Slots[4] = new EliteDrumsLayoutSlot();
            validation = EliteDrumsLayoutValidator.Validate(preset);
            Assert.That(validation.IsStructurallyValid, Is.True);
            Assert.That(validation.IsComplete, Is.True);
            Assert.That(validation.IsRuntimeSupported, Is.False);
        }

        [Test]
        public void JsonAndCopiesPreserveDataWithoutSharingSlots()
        {
            var original = EliteDrumsLayoutPreset.Full;
            var restored = JsonConvert.DeserializeObject<EliteDrumsLayoutPreset>(JsonConvert.SerializeObject(original))!;
            Assert.That(EliteDrumsLayoutValidator.Validate(restored).IsRuntimeSupported, Is.True);
            Assert.That(restored.Id, Is.EqualTo(original.Id));
            var copy = original.Copy();
            copy.Slots[0].Drum = "tom1";
            Assert.That(original.Slots[0].Drum, Is.EqualTo("snare"));
            var renamed = (EliteDrumsLayoutPreset) original.CopyWithNewName("Mine");
            Assert.That(renamed.Id, Is.Not.EqualTo(original.Id));
            Assert.That(renamed.DefaultPreset, Is.False);
            Assert.That(renamed.Name, Is.EqualTo("Mine"));
        }

        [TestCase("snare+snare")]
        [TestCase("tom1+snare")]
        [TestCase("snare+tom1+tom2")]
        [TestCase("snare+ride")]
        [TestCase("kick")]
        [TestCase("unknown")]
        [TestCase("")]
        public void MalformedDrumIdsRejectedAndRetained(string id)
        {
            var preset = new EliteDrumsLayoutPreset();
            preset.Slots[0] = EliteDrumsLayoutSlot.Ordinary(id, null);
            Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.False);
            Assert.That(EliteDrumsLayoutResolver.TryResolve(preset, out _), Is.False);
            Assert.That(preset.Slots[0].Drum, Is.EqualTo(id));
        }

        [Test]
        public void GeneratedPairsAreCanonicalDisjointAndUnsupported()
        {
            foreach (bool drum in new[] { true, false })
            {
                var sources = EliteDrumsLayoutSources.All.Where(source => drum ? EliteDrumsLayoutSources.IsDrum(source) : EliteDrumsLayoutSources.IsCymbal(source)).ToArray();
                for (int a = 0; a < sources.Length; a++)
                for (int b = a + 1; b < sources.Length; b++)
                {
                    var preset = EliteDrumsLayoutPreset.Full;
                    string id = EliteDrumsLayoutSources.PairId(sources[a], sources[b]);
                    Assert.That(EliteDrumsLayoutSources.PairId(sources[b], sources[a]), Is.EqualTo(id));
                    preset.Merges = new[] { new EliteDrumsLayoutMerge { EffectiveId = id, Sources = new[] { sources[a], sources[b] } } };
                    foreach (var slot in preset.Slots)
                    {
                        if (slot.Drum == sources[a] || slot.Drum == sources[b]) slot.Drum = null;
                        if (slot.Cymbal == sources[a] || slot.Cymbal == sources[b]) slot.Cymbal = null;
                        if (slot.Drum == null && slot.Cymbal == null) slot.Kind = EliteDrumsLayoutSlotKind.Empty;
                    }
                    preset.Slots[8] = drum ? EliteDrumsLayoutSlot.Ordinary(id, null) :
                        id.Contains("hi-hat") ? new EliteDrumsLayoutSlot { Kind = EliteDrumsLayoutSlotKind.HiHat, Cymbal = id } :
                        EliteDrumsLayoutSlot.Ordinary(null, id);
                    var validation = EliteDrumsLayoutValidator.Validate(preset);
                    Assert.That(validation.IsStructurallyValid, Is.True, id);
                    Assert.That(validation.IsComplete, Is.True, id);
                    Assert.That(validation.IsRuntimeSupported, Is.False, id);
                    Assert.That(EliteDrumsLayoutResolver.TryResolve(preset, out _), Is.False);
                    if (id.Contains("hi-hat")) Assert.That(preset.Slots[8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.HiHat));
                    var restored = JsonConvert.DeserializeObject<EliteDrumsLayoutPreset>(JsonConvert.SerializeObject(preset))!;
                    Assert.That(restored.Slots[8].Drum ?? restored.Slots[8].Cymbal, Is.EqualTo(id));
                    preset.Slots[9] = drum ? EliteDrumsLayoutSlot.Ordinary(sources[a], null) :
                        sources[a] == "hi-hat" ? EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat) : EliteDrumsLayoutSlot.Ordinary(null, sources[a]);
                    Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.False);
                }
            }
        }

        [Test]
        public void MergeDefinitionsRejectMalformedOverlappingAndChainedSources()
        {
            foreach (var sources in new[] { new[] { "snare", "snare" }, new[] { "snare", "ride" },
                         new[] { "kick", "snare" }, new[] { "unknown", "snare" },
                         new[] { "snare+tom1", "tom2" }, new[] { "snare", "tom1", "tom2" } })
            {
                var preset = EliteDrumsLayoutPreset.Full;
                preset.Merges = new[] { new EliteDrumsLayoutMerge { EffectiveId = "snare+tom1", Sources = sources } };
                Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.False);
            }
            var overlap = EliteDrumsLayoutPreset.Full;
            overlap.Merges = new[] {
                new EliteDrumsLayoutMerge { EffectiveId = "snare+tom1", Sources = new[] { "snare", "tom1" } },
                new EliteDrumsLayoutMerge { EffectiveId = "tom1+tom2", Sources = new[] { "tom1", "tom2" } } };
            Assert.That(EliteDrumsLayoutValidator.Validate(overlap).IsStructurallyValid, Is.False);
            overlap.Merges = new[] { new EliteDrumsLayoutMerge { EffectiveId = "tom1+snare", Sources = new[] { "snare", "tom1" } } };
            Assert.That(EliteDrumsLayoutValidator.Validate(overlap).IsStructurallyValid, Is.False);
            overlap.Merges[0].EffectiveId = "snare+tom1";
            var copy = overlap.Copy();
            copy.Merges[0].Sources[0] = "tom2";
            Assert.That(overlap.Merges[0].Sources[0], Is.EqualTo("snare"));
        }

        [Test]
        public void PickerReportsDisplacementAndDoesNotCreateMerges()
        {
            var before = EliteDrumsLayoutPreset.Compact;
            var after = EliteDrumsLayoutEditor.Place(before, 2, "snare");
            Assert.That(EliteDrumsLayoutEditor.GetDisplaced(before, after), Is.EqualTo(new[] { "tom1" }));
            Assert.That(EliteDrumsLayoutEditor.GetUnassigned(after), Is.EqualTo(new[] { "tom1" }));
            Assert.Throws<ArgumentException>(() => EliteDrumsLayoutEditor.Place(before, 8, "snare+tom1"));
            Assert.That(before.Merges, Is.Empty);
            Assert.That(EliteDrumsLayoutEditor.GetKickChoices(before, 8), Is.EqualTo(new[] { EliteDrumsLayoutSlotKind.Kick }));
            var combined = EliteDrumsLayoutEditor.PlaceKick(before, 8, EliteDrumsLayoutSlotKind.Kick);
            Assert.That(EliteDrumsLayoutEditor.GetKickChoices(combined, 9), Does.Contain(EliteDrumsLayoutSlotKind.Kick2));
            Assert.That(EliteDrumsLayoutEditor.GetKickChoices(combined, 8), Does.Not.Contain(EliteDrumsLayoutSlotKind.Kick2));
        }

        [Test]
        public void EveryOrdinaryDrumCymbalPairIsSupported()
        {
            foreach (var drum in EliteDrumsLayoutSources.All.Where(EliteDrumsLayoutSources.IsDrum))
            foreach (var cymbal in new[] { "left-crash", "ride", "right-crash" })
            {
                var preset = EliteDrumsLayoutPreset.Full;
                preset = EliteDrumsLayoutEditor.Place(preset, 8, drum);
                preset = EliteDrumsLayoutEditor.Place(preset, 8, cymbal);
                var resolved = EliteDrumsLayoutResolver.Resolve(preset);
                Assert.That(resolved.SourceToLane[drum], Is.EqualTo(resolved.SourceToLane[cymbal]));
            }
        }

        [Test]
        public void StructuralCompletenessAndRuntimeChecksAreSeparate()
        {
            var preset = new EliteDrumsLayoutPreset();
            var result = EliteDrumsLayoutValidator.Validate(preset);
            Assert.That(result.IsStructurallyValid, Is.True);
            Assert.That(result.IsComplete, Is.False);
            Assert.That(result.IsRuntimeSupported, Is.False);
            preset.SchemaVersion = 2;
            Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.False);
            preset.SchemaVersion = 1;
            preset.Slots = new EliteDrumsLayoutSlot[9];
            Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.False);
            preset.Slots = EliteDrumsLayoutPreset.CreateEmptySlots();
            preset.Slots[0] = null!;
            Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.False);
            preset.Slots[0] = new EliteDrumsLayoutSlot { Kind = (EliteDrumsLayoutSlotKind) 99 };
            Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.False);
        }

        [Test]
        public void ExclusiveKindsRejectPayloadsAndHiHatInOrdinary()
        {
            foreach (var kind in new[] { EliteDrumsLayoutSlotKind.Empty, EliteDrumsLayoutSlotKind.Kick,
                         EliteDrumsLayoutSlotKind.Kick1, EliteDrumsLayoutSlotKind.Kick2, EliteDrumsLayoutSlotKind.HiHat })
            {
                var preset = new EliteDrumsLayoutPreset();
                preset.Slots[0] = new EliteDrumsLayoutSlot { Kind = kind, Drum = "snare" };
                Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.False);
            }
            var ordinary = new EliteDrumsLayoutPreset();
            ordinary.Slots[0] = EliteDrumsLayoutSlot.Ordinary(null, "hi-hat");
            Assert.That(EliteDrumsLayoutValidator.Validate(ordinary).IsStructurallyValid, Is.False);
            ordinary.Slots[0] = EliteDrumsLayoutSlot.Ordinary(null, "hi-hat+ride");
            Assert.That(EliteDrumsLayoutValidator.Validate(ordinary).IsStructurallyValid, Is.False);
        }

        [Test]
        public void KickTransitionsCoverBarsCombinedSplitClearAndCollapse()
        {
            var original = EliteDrumsLayoutPreset.Compact;
            Assert.That(EliteDrumsLayoutResolver.Resolve(original).KickMode, Is.EqualTo(EliteDrumsLayoutKickMode.Bars));
            var combined = EliteDrumsLayoutEditor.PlaceKick(original, 8, EliteDrumsLayoutSlotKind.Kick);
            Assert.That(EliteDrumsLayoutResolver.Resolve(combined).KickMode, Is.EqualTo(EliteDrumsLayoutKickMode.Combined));
            var split = EliteDrumsLayoutEditor.PlaceKick(combined, 9, EliteDrumsLayoutSlotKind.Kick2);
            Assert.That(split.Slots[8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick1));
            Assert.That(EliteDrumsLayoutResolver.Resolve(split).KickMode, Is.EqualTo(EliteDrumsLayoutKickMode.Split));
            foreach (int column in new[] { 8, 9 })
            {
                var cleared = EliteDrumsLayoutEditor.Clear(split, column);
                Assert.That(cleared.Slots[column].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
                Assert.That(cleared.Slots[column == 8 ? 9 : 8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick));
                var collapsed = EliteDrumsLayoutEditor.MoveColumn(split, column, column == 8 ? 9 : 8);
                Assert.That(collapsed.Slots[column == 8 ? 9 : 8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick));
                Assert.That(collapsed.Slots[column].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
            }
            Assert.That(EliteDrumsLayoutResolver.Resolve(EliteDrumsLayoutEditor.Clear(combined, 8)).KickMode,
                Is.EqualTo(EliteDrumsLayoutKickMode.Bars));
            Assert.That(original.Slots[8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
            Assert.That(combined.Slots[8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick));
            Assert.That(EliteDrumsLayoutEditor.PlaceKick(split, 7, EliteDrumsLayoutSlotKind.Kick).Slots.Count(s => s.Kind == EliteDrumsLayoutSlotKind.Kick), Is.EqualTo(1));
        }

        [TestCase(8, EliteDrumsLayoutSlotKind.Kick2)]
        [TestCase(9, EliteDrumsLayoutSlotKind.Kick1)]
        public void KickPickerRenumbersAtomicallyAndConsolidatesAtSelectedColumn(int column, EliteDrumsLayoutSlotKind choice)
        {
            var combined = EliteDrumsLayoutEditor.PlaceKick(EliteDrumsLayoutPreset.Full, 8, EliteDrumsLayoutSlotKind.Kick);
            var split = EliteDrumsLayoutEditor.PlaceKick(combined, 9, EliteDrumsLayoutSlotKind.Kick2);
            Assert.That(EliteDrumsLayoutEditor.GetKickChoices(split, column),
                Is.EqualTo(new[] { EliteDrumsLayoutSlotKind.Kick, choice }));
            var swapped = EliteDrumsLayoutEditor.PlaceKick(split, column, choice);
            Assert.That(swapped.Slots[column].Kind, Is.EqualTo(choice));
            Assert.That(swapped.Slots[column == 8 ? 9 : 8].Kind, Is.EqualTo(
                choice == EliteDrumsLayoutSlotKind.Kick1 ? EliteDrumsLayoutSlotKind.Kick2 : EliteDrumsLayoutSlotKind.Kick1));
            Assert.That(split.Slots[8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick1));
            var consolidated = EliteDrumsLayoutEditor.PlaceKick(split, column, EliteDrumsLayoutSlotKind.Kick);
            Assert.That(consolidated.Slots[column].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Kick));
            Assert.That(consolidated.Slots[column == 8 ? 9 : 8].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
            foreach (var result in new[] { swapped, consolidated })
            {
                Assert.That(EliteDrumsLayoutValidator.Validate(result).IsRuntimeSupported, Is.True);
                Assert.That(result.Slots.Take(8).Select(s => (s.Kind, s.Drum, s.Cymbal)),
                    Is.EqualTo(split.Slots.Take(8).Select(s => (s.Kind, s.Drum, s.Cymbal))));
            }
        }

        [Test]
        public void MalformedKickConfigurationsAreRejected()
        {
            foreach (var kinds in new[] {
                new[] { EliteDrumsLayoutSlotKind.Kick1 }, new[] { EliteDrumsLayoutSlotKind.Kick2 },
                new[] { EliteDrumsLayoutSlotKind.Kick, EliteDrumsLayoutSlotKind.Kick },
                new[] { EliteDrumsLayoutSlotKind.Kick1, EliteDrumsLayoutSlotKind.Kick1 },
                new[] { EliteDrumsLayoutSlotKind.Kick, EliteDrumsLayoutSlotKind.Kick1, EliteDrumsLayoutSlotKind.Kick2 } })
            {
                var preset = new EliteDrumsLayoutPreset();
                for (int i = 0; i < kinds.Length; i++) preset.Slots[i] = EliteDrumsLayoutSlot.Exclusive(kinds[i]);
                Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.False);
            }
        }

        [Test]
        public void GeneratedKickPlacementsAlwaysNormalize()
        {
            foreach (var initial in new[] { EliteDrumsLayoutSlotKind.Kick, EliteDrumsLayoutSlotKind.Kick1, EliteDrumsLayoutSlotKind.Kick2 })
            foreach (var next in new[] { EliteDrumsLayoutSlotKind.Kick, EliteDrumsLayoutSlotKind.Kick1, EliteDrumsLayoutSlotKind.Kick2 })
            for (int destination = 0; destination < 10; destination++)
            {
                var preset = EliteDrumsLayoutEditor.PlaceKick(new EliteDrumsLayoutPreset(), 8, initial);
                preset = EliteDrumsLayoutEditor.PlaceKick(preset, destination, next);
                Assert.That(EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid, Is.True);
                for (int clear = 0; clear < 10; clear++)
                    Assert.That(EliteDrumsLayoutValidator.Validate(EliteDrumsLayoutEditor.Clear(preset, clear)).IsStructurallyValid, Is.True);
            }
        }

        [Test]
        public void DetachedMoveDisplaceExclusiveClearAndRowClear()
        {
            var original = EliteDrumsLayoutPreset.Compact;
            var moved = EliteDrumsLayoutEditor.Place(original, 2, "snare");
            Assert.That(moved.Slots[0].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
            Assert.That(moved.Slots[2].Cymbal, Is.EqualTo("left-crash"));
            Assert.That(moved.Slots[2].Drum, Is.EqualTo("snare"));
            moved = EliteDrumsLayoutEditor.Place(moved, 2, "hi-hat");
            Assert.That(moved.Slots[2].Drum, Is.Null);
            Assert.That(moved.Slots[2].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.HiHat));
            Assert.That(EliteDrumsLayoutEditor.ClearRow(moved, 2, false).Slots[2].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
            var cleared = EliteDrumsLayoutEditor.ClearRow(original, 2, true);
            Assert.That(cleared.Slots[2].Cymbal, Is.EqualTo("left-crash"));
            Assert.That(cleared.Slots[2].Drum, Is.Null);
            Assert.That(original.Slots[2].Drum, Is.EqualTo("tom1"));
            var column = EliteDrumsLayoutEditor.MoveColumn(original, 2, 0);
            Assert.That(column.Slots[0].Drum, Is.EqualTo("tom1"));
            Assert.That(column.Slots[2].Kind, Is.EqualTo(EliteDrumsLayoutSlotKind.Empty));
        }

        [Test]
        public void PreviewResolutionSupportsIncompleteMergeFreeLayoutsWithoutRelaxingActivation()
        {
            var empty = new EliteDrumsLayoutPreset();
            var emptyPreview = EliteDrumsLayoutResolver.PreviewResolve(empty);
            Assert.That(emptyPreview.Lanes, Is.Empty);
            Assert.That(emptyPreview.SourceToLane, Is.Empty);
            float emptyWidth = emptyPreview.GetLaneWidth(10);
            Assert.That(emptyWidth, Is.EqualTo(0));
            Assert.That(float.IsNaN(emptyWidth) || float.IsInfinity(emptyWidth), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => emptyPreview.GetLaneX(0));
            Assert.That(EliteDrumsLayoutResolver.TryResolve(empty, out _), Is.False);
            Assert.Throws<ArgumentException>(() => EliteDrumsLayoutResolver.Resolve(empty));

            var draft = new EliteDrumsLayoutPreset();
            draft.Slots[1] = EliteDrumsLayoutSlot.Ordinary("snare", "left-crash");
            draft.Slots[4] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            draft.Slots[7] = EliteDrumsLayoutSlot.Ordinary("tom1", null);
            var preview = EliteDrumsLayoutResolver.PreviewResolve(draft);
            Assert.That(preview.Lanes.Select(lane => lane.SavedColumn), Is.EqualTo(new[] { 1, 4, 7 }));
            Assert.That(preview.SourceToLane["snare"], Is.EqualTo(0));
            Assert.That(preview.SourceToLane["left-crash"], Is.EqualTo(0));
            Assert.That(preview.SourceToLane["tom1"], Is.EqualTo(2));
            Assert.That(preview.SourceToLane.ContainsKey("hi-hat"), Is.False);
            Assert.That(preview.KickMode, Is.EqualTo(EliteDrumsLayoutKickMode.Combined));
            Assert.That(EliteDrumsLayoutResolver.TryResolve(draft, out _), Is.False);
            Assert.Throws<ArgumentException>(() => EliteDrumsLayoutResolver.Resolve(draft));

            var splitDraft = new EliteDrumsLayoutPreset();
            splitDraft.Slots[8] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick1);
            splitDraft.Slots[9] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick2);
            Assert.That(EliteDrumsLayoutResolver.PreviewResolve(splitDraft).KickMode, Is.EqualTo(EliteDrumsLayoutKickMode.Split));
        }

        [Test]
        public void PreviewResolutionRejectsMalformedMergeStructures()
        {
            var preset = new EliteDrumsLayoutPreset { Merges = null! };
            Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(preset, out _), Is.False);

            preset = new EliteDrumsLayoutPreset { Merges = new EliteDrumsLayoutMerge[] { null! } };
            Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(preset, out _), Is.False);

            preset = new EliteDrumsLayoutPreset
            {
                Merges = new[] { new EliteDrumsLayoutMerge { EffectiveId = "snare+tom1", Sources = null! } }
            };
            Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(preset, out _), Is.False);
        }

        [Test]
        public void PreviewResolutionMatchesStrictResolutionForCompleteLayoutsAndRejectsMerges()
        {
            var complete = EliteDrumsLayoutPreset.Full;
            var strict = EliteDrumsLayoutResolver.Resolve(complete);
            var preview = EliteDrumsLayoutResolver.PreviewResolve(complete);
            Assert.That(preview.Lanes.Select(lane => lane.SavedColumn), Is.EqualTo(strict.Lanes.Select(lane => lane.SavedColumn)));
            Assert.That(preview.SourceToLane, Is.EqualTo(strict.SourceToLane));
            Assert.That(preview.KickMode, Is.EqualTo(strict.KickMode));

            complete.Merges = new[] { new EliteDrumsLayoutMerge
                { EffectiveId = "tom1+tom2", Sources = new[] { "tom1", "tom2" } } };
            complete.Slots[3].Drum = "tom1+tom2";
            complete.Slots[4] = new EliteDrumsLayoutSlot();
            Assert.That(EliteDrumsLayoutValidator.Validate(complete).IsComplete, Is.True);
            Assert.That(EliteDrumsLayoutResolver.TryResolve(complete, out _), Is.False);
            Assert.Throws<ArgumentException>(() => EliteDrumsLayoutResolver.Resolve(complete));
            Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(complete, out _), Is.False);
            Assert.Throws<ArgumentException>(() => EliteDrumsLayoutResolver.PreviewResolve(complete));
        }

        [Test]
        public void GapsCompactAndGeometryMirrorsWithoutMutatingSavedOrResolvedData()
        {
            var preset = EliteDrumsLayoutEditor.MoveColumn(EliteDrumsLayoutPreset.Compact, 0, 9);
            var resolved = EliteDrumsLayoutResolver.Resolve(preset);
            Assert.That(resolved.Lanes.Select(l => l.SavedColumn), Is.EqualTo(new[] { 1, 2, 3, 4, 9 }));
            Assert.That(resolved.SourceToLane["snare"], Is.EqualTo(4));
            Assert.That(resolved.SourceToLane["tom1"], Is.EqualTo(resolved.SourceToLane["left-crash"]));
            Assert.That(resolved.GetLaneWidth(10), Is.EqualTo(2));
            for (int lane = 0; lane < 5; lane++)
            {
                Assert.That(resolved.GetLaneX(lane, 10, true), Is.EqualTo(-resolved.GetLaneX(lane, 10)));
                Assert.That(resolved.GetLaneX(lane, 10), Is.EqualTo(lane * 2 - 4));
            }
            preset.Slots[9].Drum = "tom1";
            Assert.That(resolved.Lanes[4].Sources.Single(), Is.EqualTo("snare"));
            Assert.That(resolved.GetSourceX("snare", 10), Is.EqualTo(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => resolved.GetLaneX(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => resolved.GetLaneWidth(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => EliteDrumsLayoutEditor.Clear(EliteDrumsLayoutPreset.Full, 10));
        }
    }
}
