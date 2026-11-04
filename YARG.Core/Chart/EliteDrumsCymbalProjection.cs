// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.Chart
{
    /// <summary>Versioned, player-local cymbal targets, shared by gameplay and previews.</summary>
    public static class EliteDrumsCymbalProjection
    {
        public const byte LEGACY_POLICY = 0;
        public const byte UNORDERED_POLICY = 1;
        public const byte PHYSICAL_UNION_POLICY = 2;
        public const byte CURRENT_POLICY = 3;

        public static void ValidatePolicy(byte policy)
        {
            if (policy > CURRENT_POLICY)
                throw new InvalidDataException($"unsupported Elite cymbal merge policy: {policy}");
        }

        /// <summary>
        /// Applies only to an owned playback copy, after ordinary modifiers. Source identity,
        /// note timing and distinct physical obligations remain intact. Coincident overlapping
        /// roll targets collapse under the current policy. Reapplying is idempotent.
        /// Incomplete structurally valid editor layouts can use the same projection.
        /// </summary>
        public static void Apply(InstrumentDifficulty<EliteDrumNote> difficulty,
            EliteDrumsLayoutPreset? layout, byte policy = CURRENT_POLICY)
        {
            if (difficulty is null) throw new ArgumentNullException(nameof(difficulty));
            ValidatePolicy(policy);
            if (policy == LEGACY_POLICY || !TryGetPair(layout, policy, out var pair)) return;
            int keeper = PadForSource(pair!.KeeperSource);
            int displaced = PadForSource(pair.DisplacedSource);
            int destination = PadForSource(pair.DestinationSource);
            IReadOnlyDictionary<EliteDrumSourceDefinition, int> laneTargets = new Dictionary<EliteDrumSourceDefinition, int>();
            if (policy >= CURRENT_POLICY)
            {
                var routing = difficulty.EliteCymbalLaneRouting;
                if (routing == null || routing.Policy != policy || routing.PairChoice != pair.ChoiceId)
                {
                    routing = new EliteDrumsCymbalLaneRouting(pair.ChoiceId, policy,
                        PlanLaneTargets(difficulty, keeper, displaced, destination));
                    difficulty.SetEliteCymbalLaneRouting(routing);
                }
                laneTargets = routing.Targets;
            }
            var chords = new List<EliteDrumNote>();
            foreach (var onset in difficulty.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).GroupBy(n => n.Tick))
            {
                bool expand = onset.Any(n => n.OriginalPad == keeper) && onset.Any(n => n.OriginalPad == displaced);
                EliteDrumNote? parent = null;
                foreach (var original in onset)
                {
                    bool secondary = original.OriginalPad == displaced;
                    int target = secondary ? expand ? destination : keeper : original.OriginalPad;
                    if (secondary)
                        foreach (var source in original.SourceDefinitions)
                            if (laneTargets.TryGetValue(source, out int reserved)) { target = reserved; break; }
                    bool expanded = secondary && target != keeper;
                    var note = new EliteDrumNote(target, original.Dynamics,
                        expanded && target == (int)EliteDrumPad.HiHat ? EliteDrumsHatState.Indifferent : original.HatState,
                        original.HatPedalType, original.IsFlam, original.DrumFlags, original.Flags,
                        original.ChannelFlag, original.Time, original.Tick, original.IsDoubleKick,
                        original.SourceDefinition, original.IsFlatFlam, original.IsAuthoredFlam,
                        original.OriginalPad, expanded,
                        policy == UNORDERED_POLICY && !expanded ? original.OriginalPad : target, original.SourceDefinitions);
                    if (parent is null) parent = note;
                    else parent.AddChildNote(note);
                }
                if (parent is not null) chords.Add(parent);
            }
            difficulty.Notes.Clear();
            difficulty.Notes.AddRange(chords);
            if (policy >= PHYSICAL_UNION_POLICY) EliteDrumsPlayableLaneSegments.CollapsePhysicalDuplicates(difficulty);
            // Relink without the generic control-flag rebuild, which would erase the
            // player's chosen activation flags. Physical duplicates may have collapsed.
            for (int i = 0; i < difficulty.Notes.Count; i++)
                foreach (var note in difficulty.Notes[i].AllNotes)
                {
                    note.PreviousNote = i > 0 ? difficulty.Notes[i - 1] : null;
                    note.NextNote = i + 1 < difficulty.Notes.Count ? difficulty.Notes[i + 1] : null;
                }
            DrumPlaybackPreparer.NormalizeEliteFlams(difficulty);
        }

        // A roll reserves one target for its complete surviving span. Looking ahead
        // prevents a later primary roll from moving an already-visible secondary strip.
        // Connected secondary records share the reservation, avoiding target changes
        // inside overlapping/touching phrases. Ordinary notes keep onset-based routing.
        private static Dictionary<EliteDrumSourceDefinition, int> PlanLaneTargets(
            InstrumentDifficulty<EliteDrumNote> chart, int primary, int secondary, int destination)
        {
            var intervals = EliteDrumsPlayableLaneSegments.Resolve(chart)
                .GroupBy(segment => segment.AuthoredRecord).Select(group => (
                    Record: group.Key, Pad: (int)group.Key.AuthoredPad,
                    Start: group.Min(segment => segment.Members[0].Tick),
                    End: group.Max(segment => segment.Members[^1].Tick))).ToArray();
            var secondaryLanes = intervals.Where(lane => lane.Pad == secondary)
                .OrderBy(lane => lane.Start).ThenBy(lane => lane.End).ToArray();
            var notes = chart.Notes.SelectMany(note => note.ChildNotes.Prepend(note)).ToArray();
            int otherCymbal = new[] { (int)EliteDrumPad.LeftCrash, (int)EliteDrumPad.Ride, (int)EliteDrumPad.RightCrash }
                .Single(pad => pad != primary && pad != secondary);
            var result = new Dictionary<EliteDrumSourceDefinition, int>();
            for (int first = 0; first < secondaryLanes.Length;)
            {
                uint start = secondaryLanes[first].Start, end = secondaryLanes[first].End;
                int last = first + 1;
                while (last < secondaryLanes.Length && secondaryLanes[last].Start <= end)
                    end = Math.Max(end, secondaryLanes[last++].End);
                if (intervals.Any(lane => lane.Pad == primary && lane.Start <= end && lane.End >= start))
                {
                    var secondaryTicks = secondaryLanes.Skip(first).Take(last - first)
                        .SelectMany(lane => lane.Record.MemberSources).Select(source => source.StartTick).ToHashSet();
                    bool Occupied(int pad) =>
                        intervals.Any(lane => lane.Pad == pad && lane.Start <= end && lane.End >= start) ||
                        notes.Any(note => note.OriginalPad == pad && secondaryTicks.Contains(note.Tick));
                    int alternative = destination == (int)EliteDrumPad.HiHat ? otherCymbal : (int)EliteDrumPad.HiHat;
                    // Keep the whole strip stable. If both alternatives have other
                    // rolls, use hi-hat and the established physical-union policy.
                    // A noncoincident ordinary note remains a separate judgment and
                    // does not reserve a continuous strip of its own.
                    int target = !Occupied(destination) ? destination : !Occupied(alternative) ? alternative : (int)EliteDrumPad.HiHat;
                    for (int index = first; index < last; index++)
                        foreach (var source in secondaryLanes[index].Record.MemberSources) result[source] = target;
                }
                first = last;
            }
            return result;
        }

        public static EliteDrumsAction NormalizeAction(EliteDrumsAction action, EliteDrumsLayoutPreset? layout,
            byte policy = CURRENT_POLICY)
        {
            ValidatePolicy(policy);
            return TryGetPair(layout, policy, out var pair)
                ? NormalizeAction(action, pair) : action;
        }

        internal static bool TryGetPair(EliteDrumsLayoutPreset? layout, byte policy, out EliteDrumsCymbalPair? pair)
        {
            pair = null;
            if (policy == LEGACY_POLICY || !EliteDrumsCymbalPairs.TryGet(layout, out pair)) return false;
            return policy != UNORDERED_POLICY || EliteDrumsCymbalPairs.TryGet(pair!.EffectiveId, out pair);
        }

        internal static EliteDrumsAction NormalizeAction(EliteDrumsAction action, EliteDrumsCymbalPair? pair)
        {
            if (pair is null) return action;
            int pad = action switch
            {
                EliteDrumsAction.EliteLeftCrash => (int)EliteDrumPad.LeftCrash,
                EliteDrumsAction.EliteRide => (int)EliteDrumPad.Ride,
                EliteDrumsAction.EliteRightCrash => (int)EliteDrumPad.RightCrash,
                _ => -1
            };
            if (pad != PadForSource(pair.DisplacedSource)) return action;
            return pair.KeeperSource switch
            {
                "left-crash" => EliteDrumsAction.EliteLeftCrash,
                "ride" => EliteDrumsAction.EliteRide,
                _ => EliteDrumsAction.EliteRightCrash
            };
        }

        private static int PadForSource(string source) => source switch
        {
            "hi-hat" => (int)EliteDrumPad.HiHat,
            "left-crash" => (int)EliteDrumPad.LeftCrash,
            "ride" => (int)EliteDrumPad.Ride,
            "right-crash" => (int)EliteDrumPad.RightCrash,
            _ => throw new ArgumentException("unsupported cymbal source", nameof(source))
        };
    }
}
