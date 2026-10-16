using System;
using System.Linq;

namespace YARG.Core.Game
{
    /// <summary>Detached operations. Displaced assignments become unassigned; validation reports completeness.</summary>
    public static class EliteDrumsLayoutEditor
    {
        public static System.Collections.Generic.IReadOnlyList<string> GetUnassigned(EliteDrumsLayoutPreset preset)
        {
            if (!EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid)
                throw new ArgumentException("Expected a structurally valid layout.", nameof(preset));
            if (preset.Merges.Length != 0)
                throw new ArgumentException("Component merging is not supported by the layout editor.", nameof(preset));
            var assigned = preset.Slots.SelectMany(slot => new[] { slot.Drum, slot.Cymbal })
                .Where(id => id != null).SelectMany(id => id!.Split('+')).ToArray();
            return Array.AsReadOnly(EliteDrumsLayoutSources.All.Except(assigned).ToArray());
        }

        public static System.Collections.Generic.IReadOnlyList<string> GetDisplaced(
            EliteDrumsLayoutPreset before, EliteDrumsLayoutPreset after) =>
            Array.AsReadOnly(GetUnassigned(after).Except(GetUnassigned(before)).ToArray());

        /// <summary>Offer consolidation or a numbered assignment other than the selected cell's current number.</summary>
        public static System.Collections.Generic.IReadOnlyList<EliteDrumsLayoutSlotKind> GetKickChoices(
            EliteDrumsLayoutPreset preset, int column)
        {
            Prepare(preset, column);
            var choices = new System.Collections.Generic.List<EliteDrumsLayoutSlotKind> { EliteDrumsLayoutSlotKind.Kick };
            var current = preset.Slots[column].Kind;
            if (current == EliteDrumsLayoutSlotKind.Kick2 ||
                (current != EliteDrumsLayoutSlotKind.Kick1 && preset.Slots.Where((slot, index) => index != column)
                    .Any(slot => slot.Kind == EliteDrumsLayoutSlotKind.Kick2)))
                choices.Add(EliteDrumsLayoutSlotKind.Kick1);
            if (current == EliteDrumsLayoutSlotKind.Kick1 ||
                (current != EliteDrumsLayoutSlotKind.Kick2 && preset.Slots.Where((slot, index) => index != column)
                    .Any(slot => slot.Kind == EliteDrumsLayoutSlotKind.Kick || slot.Kind == EliteDrumsLayoutSlotKind.Kick1)))
                choices.Add(EliteDrumsLayoutSlotKind.Kick2);
            return choices.AsReadOnly();
        }

        public static EliteDrumsLayoutPreset Clear(EliteDrumsLayoutPreset preset, int column)
        {
            var copy = Prepare(preset, column);
            copy.Slots[column] = new EliteDrumsLayoutSlot();
            NormalizeKicks(copy);
            return copy;
        }

        public static EliteDrumsLayoutPreset ClearRow(EliteDrumsLayoutPreset preset, int column, bool drum)
        {
            var copy = Prepare(preset, column);
            if (copy.Slots[column].Kind != EliteDrumsLayoutSlotKind.Ordinary) return Clear(preset, column);
            if (drum) copy.Slots[column].Drum = null;
            else copy.Slots[column].Cymbal = null;
            NormalizeOrdinary(copy.Slots[column]);
            return copy;
        }

        public static EliteDrumsLayoutPreset Place(EliteDrumsLayoutPreset preset, int column, string effectiveId)
        {
            var copy = Prepare(preset, column);
            bool drum = EliteDrumsLayoutSources.TryExpand(effectiveId, true, out var sources);
            if (!drum && !EliteDrumsLayoutSources.TryExpand(effectiveId, false, out sources))
                throw new ArgumentException("Unknown or noncanonical effective ID.", nameof(effectiveId));
            if (sources.Length != 1)
                throw new ArgumentException("The phase-1 picker accepts individual sources only; saved merges are not editor commands.", nameof(effectiveId));
            // Remove overlapping assignments, including saved pairs, without changing unrelated rows.
            foreach (var slot in copy.Slots)
            {
                if (slot.Drum != null && slot.Drum.Split('+').Intersect(sources).Any()) slot.Drum = null;
                if (slot.Cymbal != null && slot.Cymbal.Split('+').Intersect(sources).Any())
                {
                    slot.Cymbal = null;
                    if (slot.Kind == EliteDrumsLayoutSlotKind.HiHat) slot.Kind = EliteDrumsLayoutSlotKind.Empty;
                }
                NormalizeOrdinary(slot);
            }
            var target = copy.Slots[column];
            if (sources.Contains("hi-hat"))
                copy.Slots[column] = new EliteDrumsLayoutSlot { Kind = EliteDrumsLayoutSlotKind.HiHat, Cymbal = effectiveId };
            else
            {
                if (target.Kind != EliteDrumsLayoutSlotKind.Ordinary) target = copy.Slots[column] = new EliteDrumsLayoutSlot();
                target.Kind = EliteDrumsLayoutSlotKind.Ordinary;
                if (drum) target.Drum = effectiveId;
                else target.Cymbal = effectiveId;
            }
            NormalizeKicks(copy);
            return copy;
        }

        public static EliteDrumsLayoutPreset PlaceKick(EliteDrumsLayoutPreset preset, int column, EliteDrumsLayoutSlotKind kind)
        {
            if (!IsKick(kind)) throw new ArgumentException("Expected Kick, Kick1 or Kick2.", nameof(kind));
            var copy = Prepare(preset, column);
            int combined = Array.FindIndex(copy.Slots, slot => slot.Kind == EliteDrumsLayoutSlotKind.Kick);
            int first = Array.FindIndex(copy.Slots, slot => slot.Kind == EliteDrumsLayoutSlotKind.Kick1);
            int second = Array.FindIndex(copy.Slots, slot => slot.Kind == EliteDrumsLayoutSlotKind.Kick2);
            foreach (var slot in copy.Slots.Where(slot => IsKick(slot.Kind))) slot.Kind = EliteDrumsLayoutSlotKind.Empty;
            if (kind == EliteDrumsLayoutSlotKind.Kick)
                copy.Slots[column] = EliteDrumsLayoutSlot.Exclusive(kind);
            else
            {
                // Renumbering an existing split kick exchanges the two positions atomically.
                int other = first == column ? second : second == column ? first :
                    kind == EliteDrumsLayoutSlotKind.Kick1 ? second : first >= 0 ? first : combined;
                if (other >= 0 && other != column)
                {
                    copy.Slots[other] = EliteDrumsLayoutSlot.Exclusive(kind == EliteDrumsLayoutSlotKind.Kick1 ?
                        EliteDrumsLayoutSlotKind.Kick2 : EliteDrumsLayoutSlotKind.Kick1);
                    copy.Slots[column] = EliteDrumsLayoutSlot.Exclusive(kind);
                }
                else copy.Slots[column] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
            }
            return copy;
        }

        /// <summary>Move a whole column, displacing its destination. Split kicks collapse when moved onto each other.</summary>
        public static EliteDrumsLayoutPreset MoveColumn(EliteDrumsLayoutPreset preset, int from, int to)
        {
            var copy = Prepare(preset, from);
            CheckColumn(to);
            if (from == to) return copy;
            var source = copy.Slots[from];
            if (IsKick(source.Kind))
            {
                if (IsKick(copy.Slots[to].Kind))
                {
                    copy.Slots[from] = new EliteDrumsLayoutSlot();
                    copy.Slots[to] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.Kick);
                    return copy;
                }
                return PlaceKick(preset, to, source.Kind);
            }
            copy.Slots[from] = new EliteDrumsLayoutSlot();
            copy.Slots[to] = source;
            NormalizeKicks(copy);
            return copy;
        }

        private static bool IsKick(EliteDrumsLayoutSlotKind kind) => kind == EliteDrumsLayoutSlotKind.Kick ||
            kind == EliteDrumsLayoutSlotKind.Kick1 || kind == EliteDrumsLayoutSlotKind.Kick2;

        private static void NormalizeKicks(EliteDrumsLayoutPreset preset)
        {
            var kicks = preset.Slots.Where(slot => IsKick(slot.Kind)).ToArray();
            if (kicks.Length == 1) kicks[0].Kind = EliteDrumsLayoutSlotKind.Kick;
        }

        private static void NormalizeOrdinary(EliteDrumsLayoutSlot slot)
        {
            if (slot.Kind == EliteDrumsLayoutSlotKind.Ordinary && slot.Drum == null && slot.Cymbal == null)
                slot.Kind = EliteDrumsLayoutSlotKind.Empty;
        }

        private static EliteDrumsLayoutPreset Prepare(EliteDrumsLayoutPreset preset, int column)
        {
            CheckColumn(column);
            if (!EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid)
                throw new ArgumentException("Edit operations require a structurally valid layout.", nameof(preset));
            if (preset.Merges.Length != 0)
                throw new ArgumentException("Component merging is not supported by the layout editor.", nameof(preset));
            return preset.Copy();
        }

        private static void CheckColumn(int column)
        {
            if (column < 0 || column >= EliteDrumsLayoutPreset.SLOT_COUNT) throw new ArgumentOutOfRangeException(nameof(column));
        }
    }
}
