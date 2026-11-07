// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Diagnostics;

namespace YARG.Core.Engine.Drums
{
    /// <summary>Resolved native source membership; never consults generic note lane flags.</summary>
    internal sealed class NativeEliteAuthoredLaneMap
    {
        internal sealed class Lane
        {
            public readonly EliteDrumNote[] Members;
            public readonly int Pad;
            public readonly EliteDrumsPlayableLaneSegment[] Segments;
            public readonly HashSet<int> EnteredTargets = new();
            public double LastRefresh = double.NegativeInfinity;
            public double EntryTime = double.NegativeInfinity;
            public bool Entered;

            public Lane(EliteDrumsPlayableLaneSegment[] segments)
            {
                Segments = segments;
                Members = segments.SelectMany(segment => segment.Members).Distinct().OrderBy(note => note.Time)
                    .ThenBy(note => note.Tick).ToArray();
                Pad = (int)segments[0].AuthoredRecord.AuthoredPad;
            }
        }

        private readonly Dictionary<EliteDrumNote, List<Lane>> _members = new();
        private readonly List<Lane> _lanes = new();
        private readonly double _laneAutohitWindow;
        private readonly double _laneProximityProtectionWindow;
        private EliteFillBarrierWindow? _barrier;
        private readonly AuthoredHandCadence _handCadence;
        public NativeEliteAuthoredLaneMap(InstrumentDifficulty<EliteDrumNote> chart, double laneAutohitWindow,
            double laneProximityProtectionWindow,
            Func<EliteDrumNote, (double FrontEnd, double BackEnd)> ordinaryWindow, bool unionPhysicalTargets = false)
        {
            using var timing = TargetedPerformance.Measure(TargetedOperation.LaneMapBuild);
            if (double.IsNaN(laneAutohitWindow) || double.IsInfinity(laneAutohitWindow) || laneAutohitWindow < 0)
                throw new ArgumentOutOfRangeException(nameof(laneAutohitWindow));
            if (double.IsNaN(laneProximityProtectionWindow) || double.IsInfinity(laneProximityProtectionWindow) || laneProximityProtectionWindow < 0)
                throw new ArgumentOutOfRangeException(nameof(laneProximityProtectionWindow));
            _laneProximityProtectionWindow = laneProximityProtectionWindow;
            _laneAutohitWindow = laneAutohitWindow;
            var segments = unionPhysicalTargets ? EliteDrumsPlayableLaneSegments.ResolvePhysical(chart) :
                EliteDrumsPlayableLaneSegments.Resolve(chart);
            // Current playback joins authored records connected by overlapping physical
            // intervals. Archived policy keeps each authored phrase's independent entrance.
            var records = segments.SelectMany(segment => segment.AuthoredRecords).Distinct().ToArray();
            var groups = records.ToDictionary(record => record, record => new HashSet<EliteDrumNativeAuthoredLaneRecord> { record });
            foreach (var segment in segments)
            {
                var merged = new HashSet<EliteDrumNativeAuthoredLaneRecord>(segment.AuthoredRecords.SelectMany(record => groups[record]));
                foreach (var record in merged) groups[record] = merged;
            }
            foreach (var group in groups.Values.Distinct())
            {
                var lane = new Lane(segments.Where(segment => segment.AuthoredRecords.Any(group.Contains)).ToArray());
                _lanes.Add(lane);
                foreach (var note in lane.Members)
                {
                    if (!_members.TryGetValue(note, out var lanes)) _members[note] = lanes = new List<Lane>();
                    lanes.Add(lane);
                }
            }
            _handCadence = new AuthoredHandCadence(segments.Where(segment => IsHand(segment.Pad))
                .Select(segment => new AuthoredHandCadence.Lane(segment.Pad, segment.Members.Select(n => n.Time).ToArray(),
                    segment.StartTime + ordinaryWindow(segment.Members[0]).FrontEnd,
                    segment.EndTime + ordinaryWindow(segment.Members[^1]).BackEnd)));
        }

        // Membership is immutable for this projected chart; cadence/entry state is not.
        public bool Contains(EliteDrumNote note) => _members.ContainsKey(note);

        public void Reset()
        {
            foreach (var lane in _lanes)
            {
                lane.Entered = false;
                lane.EnteredTargets.Clear();
                lane.LastRefresh = double.NegativeInfinity;
                lane.EntryTime = double.NegativeInfinity;
            }
            _barrier = null;
            _handCadence.Reset();
        }

        private static bool IsHand(int pad) => pad != (int)EliteDrumNote.EliteDrumPad.Kick &&
            pad != (int)EliteDrumNote.EliteDrumPad.HatPedal;

        public void Input(int pad, double time)
        {
            if (IsHand(pad)) _handCadence.Input(pad, time, _laneAutohitWindow);
        }

        public void LatchBarrier(double time)
            => _barrier = EliteFillBarrierCalculator.Calculate(time, EliteFillPolicyV1.Default.Parameters);

        public void Refresh(EliteDrumNote note, double time)
        {
            if (!_members.TryGetValue(note, out var lanes)) return;
            foreach (var lane in lanes)
            {
                // Only the first authored member enters a lane. A strike on an
                // overlapping later member cannot bootstrap another phrase.
                if (!lane.Entered && ReferenceEquals(lane.Members[0], note))
                {
                    lane.Entered = true;
                    lane.EntryTime = time;
                }
                if (lane.Entered)
                {
                    // A later real member may enter a new destination even if its first
                    // occurrence was missed. Returning targets keep their entry, but still
                    // require valid current physical cadence at the note's own timestamp.
                    lane.EnteredTargets.Add(note.Pad);
                    lane.LastRefresh = time;
                }
            }
        }

        public bool ProtectStrike(int pad, double time)
        {
            bool protectedStrike = false;
            foreach (var lane in _lanes)
            {
                if (!lane.Entered || !lane.EnteredTargets.Contains(pad) || time < lane.EntryTime) continue;
                foreach (var segment in lane.Segments)
                {
                    if (segment.Pad != pad || time < segment.StartTime && !segment.Members[0].WasHit) continue;
                    if (segment.Members[^1].WasHit && time > segment.EndTime)
                    {
                        protectedStrike |= time - segment.EndTime < _laneProximityProtectionWindow;
                        continue;
                    }
                    if (time > EliteFillPolicyV1.Default.DeadlineAt(segment.EndTime).GraceUntil) continue;
                    // Protection follows a physical activity interval, never a synthetic
                    // same-pad bridge across another target. Entry persists for covered
                    // returns, while barriers still govern automatic source continuation.
                    if (_barrier is not { } barrier || barrier.At(time) == EliteFillBarrierPhase.Clear ||
                        lane.EntryTime >= barrier.TriggerTimestamp)
                        lane.LastRefresh = time;
                    protectedStrike = true;
                }
            }
            return protectedStrike;
        }

        public void Miss(EliteDrumNote note)
        {
            if (!_members.TryGetValue(note, out var lanes)) return;
            foreach (var lane in lanes)
            {
                // A miss breaks combo, but subsequent physical inputs can recover cadence.
                lane.LastRefresh = double.NegativeInfinity;
            }
        }

        public bool CanContinue(EliteDrumNote note, double judgmentTime)
        {
            if (!_members.TryGetValue(note, out var lanes)) return false;
            foreach (var lane in lanes)
            {
                if (ReferenceEquals(lane.Members[0], note) || !lane.Entered || !lane.EnteredTargets.Contains(note.Pad) ||
                    lane.Members[^1].WasHit || lane.Members[^1].WasMissed) continue;
                if (_barrier is { } barrier && barrier.At(judgmentTime) != EliteFillBarrierPhase.Clear &&
                    lane.EntryTime < barrier.TriggerTimestamp) continue;
                double expiry = lane.LastRefresh + _laneAutohitWindow;
                if (_laneAutohitWindow > 0 && (IsHand(lane.Pad)
                    ? _handCadence.Covers(note.Pad, note.Time)
                    : note.Time <= expiry && (lane.Pad == (int) EliteDrumNote.EliteDrumPad.Kick ||
                        judgmentTime <= expiry)))
                    return true;
            }
            return false;
        }

        public double? NextContinuationDeadline(EliteDrumNote note, double handDeadline)
        {
            bool member = _members.TryGetValue(note, out var lanes);
            TargetedPerformance.Current?.RecordLaneLookup(member);
            if (!member) return null;
            double? earliest = null;
            foreach (var lane in lanes!)
            {
                if (ReferenceEquals(lane.Members[0], note) || !lane.Entered || !lane.EnteredTargets.Contains(note.Pad) ||
                    lane.Members[^1].WasHit || lane.Members[^1].WasMissed) continue;
                if (IsHand(lane.Pad))
                {
                    if (_handCadence.Covers(note.Pad, note.Time)) return handDeadline;
                    continue;
                }
                double expiry = lane.LastRefresh + _laneAutohitWindow;
                if (_laneAutohitWindow <= 0 || note.Time > expiry) continue;
                // Kicks are an independent single-pad tremolo. Judge coverage at the
                // note timestamp, after allowing its ordinary physical hit window.
                // Only real kick strikes refresh this lane's clock; hands cannot.
                if (lane.Pad == (int) EliteDrumNote.EliteDrumPad.Kick) return handDeadline;
                double deadline = Math.Min(note.Time + EliteFillPolicyV1.Default.Parameters.CadenceStepSeconds,
                    expiry);
                if (earliest is null || deadline < earliest) earliest = deadline;
            }
            return earliest;
        }
    }
}
