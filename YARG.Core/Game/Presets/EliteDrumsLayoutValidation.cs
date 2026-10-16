using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Game
{
    public static class EliteDrumsLayoutSources
    {
        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
            { "snare", "tom1", "tom2", "tom3", "left-crash", "ride", "right-crash", "hi-hat" });

        public static bool IsDrum(string source) => source == "snare" || source == "tom1" || source == "tom2" || source == "tom3";
        public static bool IsCymbal(string source) => source == "left-crash" || source == "ride" || source == "right-crash" || source == "hi-hat";
        public static string PairId(string first, string second)
        {
            if (first == second || !(IsDrum(first) && IsDrum(second) || IsCymbal(first) && IsCymbal(second)))
                throw new ArgumentException("A pair requires two distinct sources of the same category.");
            return string.CompareOrdinal(first, second) < 0 ? first + "+" + second : second + "+" + first;
        }

        public static bool TryExpand(string? id, bool drum, out string[] sources)
        {
            sources = Array.Empty<string>();
            if (id == null) return false;
            var parts = id.Split('+');
            if (parts.Length < 1 || parts.Length > 2 || parts.Any(part => drum ? !IsDrum(part) : !IsCymbal(part))) return false;
            if (parts.Length == 2 && (parts[0] == parts[1] || id != PairId(parts[0], parts[1]))) return false;
            sources = parts;
            return true;
        }
    }

    public sealed class EliteDrumsLayoutValidation
    {
        public bool IsStructurallyValid { get; }
        public bool IsComplete { get; }
        public bool IsRuntimeSupported { get; }
        public IReadOnlyList<string> Errors { get; }
        internal EliteDrumsLayoutValidation(bool structural, bool complete, bool supported, List<string> errors)
        {
            IsStructurallyValid = structural;
            IsComplete = complete;
            IsRuntimeSupported = supported;
            Errors = errors.AsReadOnly();
        }
    }

    public static class EliteDrumsLayoutValidator
    {
        public static EliteDrumsLayoutValidation Validate(EliteDrumsLayoutPreset? preset)
        {
            var errors = new List<string>();
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            bool supported = true;
            int kick = 0, kick1 = 0, kick2 = 0;
            if (preset == null || preset.SchemaVersion != EliteDrumsLayoutPreset.CURRENT_SCHEMA_VERSION ||
                preset.Slots == null || preset.Slots.Length != EliteDrumsLayoutPreset.SLOT_COUNT)
            {
                errors.Add("Expected schema v1 and exactly ten slots.");
                return new EliteDrumsLayoutValidation(false, false, false, errors);
            }
            var mergeIds = new HashSet<string>(StringComparer.Ordinal);
            var mergedSources = new HashSet<string>(StringComparer.Ordinal);
            if (preset.Merges == null) errors.Add("Merge definitions are null.");
            else foreach (var merge in preset.Merges)
            {
                if (merge == null || merge.Sources == null || merge.Sources.Length != 2 ||
                    !(EliteDrumsLayoutSources.IsDrum(merge.Sources[0]) && EliteDrumsLayoutSources.IsDrum(merge.Sources[1]) ||
                      EliteDrumsLayoutSources.IsCymbal(merge.Sources[0]) && EliteDrumsLayoutSources.IsCymbal(merge.Sources[1])) ||
                    merge.Sources[0] == merge.Sources[1])
                {
                    errors.Add("A merge requires two distinct same-category source IDs.");
                    continue;
                }
                if (merge.EffectiveId != EliteDrumsLayoutSources.PairId(merge.Sources[0], merge.Sources[1]) ||
                    !mergeIds.Add(merge.EffectiveId)) errors.Add("Merge ID is duplicate or noncanonical.");
                foreach (string source in merge.Sources)
                    if (!mergedSources.Add(source)) errors.Add("Merge definitions must be disjoint.");
                supported = false;
            }
            for (int i = 0; i < preset.Slots.Length; i++)
            {
                var slot = preset.Slots[i];
                if (slot == null) { errors.Add($"Slot {i} is null."); continue; }
                bool valid = true;
                switch (slot.Kind)
                {
                    case EliteDrumsLayoutSlotKind.Empty:
                        valid = slot.Drum == null && slot.Cymbal == null;
                        break;
                    case EliteDrumsLayoutSlotKind.Ordinary:
                        valid = slot.Drum != null || slot.Cymbal != null;
                        if (slot.Drum != null) valid &= Add(slot.Drum, true);
                        if (slot.Cymbal != null)
                        {
                            valid &= Add(slot.Cymbal, false);
                            valid &= !slot.Cymbal.Split('+').Contains("hi-hat");
                        }
                        break;
                    case EliteDrumsLayoutSlotKind.HiHat:
                        valid = slot.Drum == null && slot.Cymbal != null;
                        if (slot.Cymbal != null)
                        {
                            valid &= Add(slot.Cymbal, false);
                            valid &= slot.Cymbal.Split('+').Contains("hi-hat");
                        }
                        break;
                    case EliteDrumsLayoutSlotKind.Kick: kick++; valid = slot.Drum == null && slot.Cymbal == null; break;
                    case EliteDrumsLayoutSlotKind.Kick1: kick1++; valid = slot.Drum == null && slot.Cymbal == null; break;
                    case EliteDrumsLayoutSlotKind.Kick2: kick2++; valid = slot.Drum == null && slot.Cymbal == null; break;
                    default: valid = false; break;
                }
                if (!valid) errors.Add($"Slot {i} has invalid or duplicate assignments.");
            }
            if (!(kick == 0 && kick1 == 0 && kick2 == 0 || kick == 1 && kick1 == 0 && kick2 == 0 ||
                  kick == 0 && kick1 == 1 && kick2 == 1)) errors.Add("Kick slots must be absent, combined, or one complete split pair.");
            bool structural = errors.Count == 0;
            bool complete = structural && EliteDrumsLayoutSources.All.All(assigned.Contains);
            return new EliteDrumsLayoutValidation(structural, complete, structural && complete && supported, errors);

            bool Add(string id, bool drum)
            {
                if (!EliteDrumsLayoutSources.TryExpand(id, drum, out var sources)) return false;
                // A declared pair replaces its two sources in the effective-piece set.
                if (sources.Length == 1 && mergedSources.Contains(id)) return false;
                if (sources.Length > 1)
                {
                    supported = false;
                    if (!mergeIds.Contains(id)) return false;
                }
                bool unique = true;
                foreach (var source in sources) unique &= assigned.Add(source);
                return unique;
            }
        }
    }
}
