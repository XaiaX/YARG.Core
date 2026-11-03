// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Game
{
    /// <summary>A primary shared target and deterministic simultaneous-note expansion.</summary>
    public sealed class EliteDrumsCymbalPair
    {
        public string EffectiveId { get; }
        public string ChoiceId { get; }
        public IReadOnlyList<string> Sources { get; }
        public string PrimarySource { get; }
        public string SecondarySource { get; }
        public string KeeperSource => PrimarySource;
        public string DisplacedSource => SecondarySource;
        public string DestinationSource { get; }

        internal EliteDrumsCymbalPair(string primary, string secondary)
        {
            EffectiveId = EliteDrumsLayoutSources.PairId(primary, secondary);
            ChoiceId = primary + ">" + secondary;
            Sources = Array.AsReadOnly(EffectiveId.Split('+'));
            PrimarySource = primary;
            SecondarySource = secondary;
            DestinationSource = secondary == "left-crash" ? "hi-hat" :
                new[] { "left-crash", "ride", "right-crash" }.Single(source => source != primary && source != secondary);
        }
    }

    public static class EliteDrumsCymbalPairs
    {
        public static IReadOnlyList<EliteDrumsCymbalPair> All { get; } = Array.AsReadOnly(new[]
        {
            new EliteDrumsCymbalPair("left-crash", "right-crash"),
            new EliteDrumsCymbalPair("left-crash", "ride"),
            new EliteDrumsCymbalPair("ride", "left-crash"),
            new EliteDrumsCymbalPair("ride", "right-crash"),
            new EliteDrumsCymbalPair("right-crash", "left-crash"),
            new EliteDrumsCymbalPair("right-crash", "ride")
        });

        /// <summary>Canonical unordered IDs retain their original keeper orientation.</summary>
        public static bool TryGet(string? effectiveId, out EliteDrumsCymbalPair? pair)
        {
            string keeper = effectiveId == "left-crash+ride" ? "ride" : "right-crash";
            pair = All.FirstOrDefault(candidate => candidate.EffectiveId == effectiveId && candidate.PrimarySource == keeper);
            return pair != null;
        }

        public static bool TryGetChoice(string? choiceId, out EliteDrumsCymbalPair? pair)
        {
            pair = All.FirstOrDefault(candidate => candidate.ChoiceId == choiceId);
            return pair != null;
        }

        public static bool TryGetMerge(EliteDrumsLayoutMerge? merge, out EliteDrumsCymbalPair? pair)
        {
            pair = null;
            if (merge == null) return false;
            if (merge.PrimarySource == null) return TryGet(merge.EffectiveId, out pair);
            pair = All.FirstOrDefault(candidate => candidate.EffectiveId == merge.EffectiveId &&
                candidate.PrimarySource == merge.PrimarySource);
            return pair != null;
        }

        /// <summary>Archived generic merge data stays loadable, but cannot enter editing or playback.</summary>
        public static bool HasOnlySupportedMerges(EliteDrumsLayoutPreset? preset) => preset?.Merges != null &&
            preset.Merges.All(merge => TryGetMerge(merge, out _));

        /// <summary>Also accepts incomplete editor drafts with an assigned supported pair.</summary>
        public static bool TryGet(EliteDrumsLayoutPreset? preset, out EliteDrumsCymbalPair? pair)
        {
            pair = null;
            return HasOnlySupportedMerges(preset) && preset!.Merges.Length == 1 &&
                EliteDrumsLayoutValidator.Validate(preset).IsStructurallyValid &&
                preset.Slots.Any(slot => slot.Cymbal == preset.Merges[0].EffectiveId) &&
                TryGetMerge(preset.Merges[0], out pair);
        }
    }
}
