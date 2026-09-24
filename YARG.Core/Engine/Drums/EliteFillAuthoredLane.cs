using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Logging;

namespace YARG.Core.Engine.Drums
{
    /// <summary>
    /// Immutable runtime identity for one authored Elite hand lane: the ordered final
    /// physical members of a single authored lane phrase record. Authored membership is
    /// the only lane identity V1 trusts at runtime — flattened generic lane flags cannot
    /// distinguish overlapping lanes. A physical note may belong to several lanes, and
    /// membership never changes after construction.
    /// </summary>
    public sealed class EliteFillAuthoredLane
    {
        private readonly DrumNote[] _members;

        public string GameplayId { get; }

        /// <summary>Members in chart order; the first member is the lane's entry note.</summary>
        public IReadOnlyList<DrumNote> Members => _members;

        public DrumNote StartNote => _members[0];

        public EliteFillAuthoredLane(string gameplayId, IEnumerable<DrumNote> members)
        {
            if (string.IsNullOrWhiteSpace(gameplayId))
                throw new ArgumentException("A gameplay ID is required.", nameof(gameplayId));
            _members = (members ?? throw new ArgumentNullException(nameof(members))).Distinct().ToArray();
            if (_members.Length < EliteDrumAuthoredLanePhraseTypes.MinimumSurvivingMembers)
                throw new ArgumentException("An authored lane needs the minimum surviving member count.", nameof(members));
            GameplayId = gameplayId;
        }
    }

    /// <summary>
    /// Scheduler input for authored Elite hand lanes. It separates two facts the
    /// scheduler must never conflate:
    ///  - <see cref="Lanes"/> are the resolved runtime lanes that may grant cadence
    ///    coverage through their entry note.
    ///  - <see cref="AuthoredMembershipNotes"/> are the physical notes claimed by ANY
    ///    authored lane record, resolved or not. Claimed membership is the fail-closed
    ///    boundary: a note an authored record described may never fall back to the
    ///    generic LaneStart/LaneEnd approximation, even when its record was unresolved
    ///    or its lane was dropped.
    /// Physical notes outside <see cref="AuthoredMembershipNotes"/> carry no authored
    /// metadata at all and keep the legacy flag-based lane behavior.
    /// </summary>
    public sealed class EliteFillAuthoredLaneMap
    {
        public IReadOnlyList<EliteFillAuthoredLane> Lanes { get; }

        /// <summary>Physical notes claimed by any authored lane record, valid or not.</summary>
        public IReadOnlyCollection<DrumNote> AuthoredMembershipNotes { get; }

        /// <summary>
        /// Direct-lane construction for callers that already hold validated lanes.
        /// Membership defaults to the union of those lanes' members, because the lanes
        /// are the authored records' runtime form.
        /// </summary>
        public EliteFillAuthoredLaneMap(IReadOnlyList<EliteFillAuthoredLane>? lanes,
            IEnumerable<DrumNote>? authoredMembershipNotes = null)
        {
            Lanes = lanes ?? Array.Empty<EliteFillAuthoredLane>();
            if (authoredMembershipNotes is not null)
            {
                AuthoredMembershipNotes = authoredMembershipNotes.ToHashSet();
                return;
            }

            var claimed = new HashSet<DrumNote>();
            foreach (var lane in Lanes)
            {
                claimed.UnionWith(lane.Members);
            }

            AuthoredMembershipNotes = claimed;
        }

        /// <summary>
        /// Builds the scheduler-side authored lane map from authored phrase records.
        /// Records whose final identity is unresolved, or that no longer resolve to the
        /// minimum surviving physical members, contribute no runtime lane: an
        /// unsupported construct fails closed and grants no runtime lane coverage. Their
        /// surviving members still land in <see cref="AuthoredMembershipNotes"/>, so
        /// those notes never re-enter through the generic lane flags either. Duplicate
        /// origins are deduplicated before the member-count check, and malformed lanes
        /// are warned about and skipped rather than thrown on.
        /// </summary>
        public static EliteFillAuthoredLaneMap Build(IReadOnlyList<DrumNote> physicalNotes,
            IReadOnlyList<EliteDrumVisualDescriptorV1>? authoredLaneRecords)
        {
            if (authoredLaneRecords is null || authoredLaneRecords.Count == 0)
            {
                return new EliteFillAuthoredLaneMap(null);
            }

            var notesByOrigin = new Dictionary<EliteDrumConversionOrigin, List<DrumNote>>();
            foreach (var note in physicalNotes)
            {
                if (note.ConversionOrigin is not { } origin) continue;
                if (!notesByOrigin.TryGetValue(origin, out var bucket))
                {
                    bucket = new List<DrumNote>();
                    notesByOrigin.Add(origin, bucket);
                }
                bucket.Add(note);
            }

            // Every record claims its surviving members, resolved or not. This is what
            // keeps an unresolved or dropped lane from re-entering through flags.
            var claimedNotes = new HashSet<DrumNote>();
            foreach (var record in authoredLaneRecords)
            {
                foreach (var origin in record.Origins)
                {
                    if (notesByOrigin.TryGetValue(origin, out var bucket))
                    {
                        claimedNotes.UnionWith(bucket);
                    }
                }
            }

            var lanes = new List<EliteFillAuthoredLane>();
            foreach (var record in authoredLaneRecords)
            {
                // Unresolved records are provenance only; they never carry runtime lanes.
                if (!record.IsFinalIdentityResolved) continue;

                // Deduplicate origins first: a record may repeat an origin, and each
                // surviving physical member must be counted exactly once before the
                // minimum-member check below.
                var members = new List<DrumNote>();
                foreach (var origin in record.Origins.Distinct())
                {
                    if (notesByOrigin.TryGetValue(origin, out var bucket)) members.AddRange(bucket);
                }

                var uniqueMembers = members.Distinct()
                    .OrderBy(note => note.Time)
                    .ThenBy(note => note.Tick)
                    .ToArray();
                if (uniqueMembers.Length < EliteDrumAuthoredLanePhraseTypes.MinimumSurvivingMembers)
                {
                    YargLogger.LogWarning(
                        $"Authored Elite lane record {record.GameplayId} kept only {uniqueMembers.Length} unique surviving physical members; no runtime lane is granted.");
                    continue;
                }

                lanes.Add(new EliteFillAuthoredLane(record.GameplayId, uniqueMembers));
            }

            return new EliteFillAuthoredLaneMap(lanes, claimedNotes);
        }
    }
}
