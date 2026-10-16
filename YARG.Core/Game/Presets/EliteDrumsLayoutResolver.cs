using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YARG.Core.Game
{
    public enum EliteDrumsLayoutKickMode { Bars, Combined, Split }

    public sealed class EliteDrumsResolvedLane
    {
        public int SavedColumn { get; }
        public int Index { get; }
        public EliteDrumsLayoutSlotKind Kind { get; }
        public IReadOnlyList<string> Sources { get; }
        public IReadOnlyList<string> EffectiveIds { get; }
        internal EliteDrumsResolvedLane(int column, int index, EliteDrumsLayoutSlot slot)
        {
            SavedColumn = column;
            Index = index;
            Kind = slot.Kind;
            var ids = new[] { slot.Drum, slot.Cymbal }.Where(id => id != null).Cast<string>().ToArray();
            if (ids.Length == 0) ids = new[] { slot.Kind == EliteDrumsLayoutSlotKind.Kick ? "kick" :
                slot.Kind == EliteDrumsLayoutSlotKind.Kick1 ? "kick1" : "kick2" };
            EffectiveIds = Array.AsReadOnly(ids);
            Sources = Array.AsReadOnly(ids.SelectMany(id => id.Split('+')).ToArray());
        }
    }

    /// <summary>Immutable presentation lookup, detached from saved data. Coordinates are centered and width-scaled.</summary>
    public sealed class EliteDrumsResolvedLayout
    {
        public IReadOnlyList<EliteDrumsResolvedLane> Lanes { get; }
        public IReadOnlyDictionary<string, int> SourceToLane { get; }
        public IReadOnlyDictionary<int, int> SavedColumnToLane { get; }
        public EliteDrumsLayoutKickMode KickMode { get; }
        internal EliteDrumsResolvedLayout(List<EliteDrumsResolvedLane> lanes)
        {
            Lanes = lanes.AsReadOnly();
            var sources = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var lane in lanes)
                foreach (string source in lane.Sources) sources.Add(source, lane.Index);
            SourceToLane = new ReadOnlyDictionary<string, int>(sources);
            SavedColumnToLane = new ReadOnlyDictionary<int, int>(lanes.ToDictionary(lane => lane.SavedColumn, lane => lane.Index));
            KickMode = lanes.Any(lane => lane.Kind == EliteDrumsLayoutSlotKind.Kick) ? EliteDrumsLayoutKickMode.Combined :
                lanes.Any(lane => lane.Kind == EliteDrumsLayoutSlotKind.Kick1) ? EliteDrumsLayoutKickMode.Split : EliteDrumsLayoutKickMode.Bars;
        }

        public float GetLaneWidth(float highwayWidth)
        {
            if (float.IsNaN(highwayWidth) || float.IsInfinity(highwayWidth) || highwayWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(highwayWidth));
            return Lanes.Count == 0 ? 0 : highwayWidth / Lanes.Count;
        }

        public float GetLaneX(int lane, float highwayWidth = 1, bool lefty = false)
        {
            if (lane < 0 || lane >= Lanes.Count) throw new ArgumentOutOfRangeException(nameof(lane));
            float x = (lane + 0.5f) * GetLaneWidth(highwayWidth) - highwayWidth / 2;
            return lefty ? -x : x;
        }

        public float GetSourceX(string source, float highwayWidth = 1, bool lefty = false) =>
            GetLaneX(SourceToLane[source], highwayWidth, lefty);
    }

    public static class EliteDrumsLayoutResolver
    {
        public static bool TryResolve(EliteDrumsLayoutPreset? preset, out EliteDrumsResolvedLayout? resolved)
        {
            resolved = null;
            if (!EliteDrumsLayoutValidator.Validate(preset).IsRuntimeSupported) return false;
            resolved = BuildResolvedLayout(preset!);
            return true;
        }

        public static EliteDrumsResolvedLayout Resolve(EliteDrumsLayoutPreset preset)
        {
            if (!TryResolve(preset, out var resolved)) throw new ArgumentException("Layout is not runtime supported.", nameof(preset));
            return resolved!;
        }

        /// <summary>Resolves a structurally valid, merge-free layout for display while it is being edited.</summary>
        public static bool TryPreviewResolve(EliteDrumsLayoutPreset? preset, out EliteDrumsResolvedLayout? resolved)
        {
            resolved = null;
            var validation = EliteDrumsLayoutValidator.Validate(preset);
            if (!validation.IsStructurallyValid || preset!.Merges.Length != 0) return false;
            resolved = BuildResolvedLayout(preset);
            return true;
        }

        /// <summary>Resolves a structurally valid, merge-free layout for display while it is being edited.</summary>
        public static EliteDrumsResolvedLayout PreviewResolve(EliteDrumsLayoutPreset preset)
        {
            if (!TryPreviewResolve(preset, out var resolved))
                throw new ArgumentException("Layout is not valid for preview resolution.", nameof(preset));
            return resolved!;
        }

        private static EliteDrumsResolvedLayout BuildResolvedLayout(EliteDrumsLayoutPreset preset)
        {
            var lanes = new List<EliteDrumsResolvedLane>();
            for (int column = 0; column < preset.Slots.Length; column++)
                if (preset.Slots[column].Kind != EliteDrumsLayoutSlotKind.Empty)
                    lanes.Add(new EliteDrumsResolvedLane(column, lanes.Count, preset.Slots[column]));
            return new EliteDrumsResolvedLayout(lanes);
        }
    }
}
