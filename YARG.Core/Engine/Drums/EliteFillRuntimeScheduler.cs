using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;

namespace YARG.Core.Engine.Drums
{
    /// <summary>
    /// Engine-side physical-note journal for Elite Fill V1. Adjudication is terminal
    /// immediately, while commits are released in chart order so a later child or
    /// parent can never strand an earlier physical note.
    /// </summary>
    public sealed class EliteFillRuntimeScheduler
    {
        public readonly struct Commit
        {
            public DrumNote Note { get; }
            public bool WasHit { get; }

            public Commit(DrumNote note, bool wasHit)
            {
                Note = note;
                WasHit = wasHit;
            }
        }

        private readonly IReadOnlyList<DrumNote> _physicalNotes;
        private readonly Dictionary<DrumNote, int> _ordinals;
        private readonly EliteFillDeadline[] _deadlines;
        private readonly bool[] _terminal;
        private readonly bool[] _committed;
        private readonly bool[] _outcomes;
        private readonly EliteFillPolicyV1 _policy;

        // Authored lane identity (nullable: present only for converted Elite charts).
        // _noteLanes[i] lists every authored lane that owns _physicalNotes[i]; coverage
        // is tracked per lane through the lane's entry note, so overlapping lanes never
        // borrow or break each other's continuation state. _authoredMembership[i] marks
        // notes claimed by ANY authored lane record, resolved or not: claimed notes never
        // use the generic flag fallback, so unresolved or dropped lanes fail closed.
        private readonly int[][] _noteLanes;
        private readonly EliteFillAuthoredLane[] _authoredLanes;
        private readonly bool[] _authoredMembership;

        // Per-authored-lane input-driven protection state. _laneLastInput[lane] is the
        // timestamp of the most recent qualifying real input for that lane (an exact
        // physical hit on one of its members, or a protected in-lane strike on one of
        // its pads). Lane entry alone grants no protection: like the native lane input
        // policy (BaseEngine.SubmitLaneNote + UpdateLaneAutohitExpireTime), the refresh
        // is bounded by a window the caller supplies — the engine uses
        // HitWindow.LaneAutohitWindow, the same bounded window native lanes use.
        private readonly double[] _laneLastInput;
        private readonly bool[] _laneHasInput;
        private readonly double[] _laneEntryTime;
        private readonly bool[] _laneHasEntryTime;
        private readonly int[][] _lanePads;
        private readonly double[] _laneActiveUntil;
        private double _laneAutohitWindowSeconds;

        private EliteFillBarrierWindow? _barrier;
        private int _nextCommit;

        public IReadOnlyList<DrumNote> PhysicalNotes => _physicalNotes;
        /// <summary>Authored runtime lanes in construction order; empty when the chart carries none.</summary>
        public IReadOnlyList<EliteFillAuthoredLane> AuthoredLanes => _authoredLanes;
        public bool HasAuthoredMembership => _authoredMembership.Any(member => member);
        public int AuthoredMembershipCount => _authoredMembership.Count(member => member);

        public void SetLaneAutohitWindow(double seconds)
        {
            RequireFinite(seconds, nameof(seconds));
            _laneAutohitWindowSeconds = Math.Max(0, seconds);
        }
        public int TerminalCount { get; private set; }
        public int CommittedCount { get; private set; }
        public int PendingCount => TerminalCount - CommittedCount;
        public EliteFillBarrierPhase BarrierPhase(double timestamp)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            return _barrier?.At(timestamp) ?? EliteFillBarrierPhase.Clear;
        }
        public bool BarrierLatched(double timestamp) => BarrierPhase(timestamp) != EliteFillBarrierPhase.Clear;

        /// <summary>
        /// Returns whether V1 may resolve a note without a matching real input. A
        /// participation barrier affects only continuation/autohit/cadence; ordinary
        /// real inputs remain eligible through <see cref="FindCandidate"/>.
        /// </summary>
        public bool AutomaticContinuationAllowed(double timestamp)
            => BarrierPhase(timestamp) == EliteFillBarrierPhase.Clear;

        private bool AutomaticContinuationAllowed(DrumNote note, double timestamp)
        {
            if (AutomaticContinuationAllowed(timestamp)) return true;
            if (note is null || !_ordinals.TryGetValue(note, out var ordinal) || !_authoredMembership[ordinal])
                return false;

            // A real entry into this authored phrase starts its own bounded continuation
            // window even while an older participation barrier is recovering. It does not
            // clear the barrier or authorize any other lane / metadata-free note.
            var barrier = _barrier;
            if (barrier is null) return false;
            foreach (var laneId in _noteLanes[ordinal])
            {
                if (_laneHasEntryTime[laneId] && _laneEntryTime[laneId] >= barrier.TriggerTimestamp)
                    return true;
            }

            return false;
        }

        public EliteFillRuntimeScheduler(IEnumerable<DrumNote> chartNotes, EliteFillPolicyV1 policy,
            Func<DrumNote, (double FrontEnd, double BackEnd)>? ordinaryWindow = null,
            EliteFillAuthoredLaneMap? authoredLaneMap = null)
        {
            if (chartNotes is null) throw new ArgumentNullException(nameof(chartNotes));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));

            var physical = new List<DrumNote>();
            foreach (var parent in chartNotes)
            {
                foreach (var note in parent.AllNotes)
                {
                    physical.Add(note);
                }
            }

            _physicalNotes = physical;
            _ordinals = new Dictionary<DrumNote, int>(physical.Count);
            _deadlines = new EliteFillDeadline[physical.Count];
            for (var i = 0; i < physical.Count; i++)
            {
                _ordinals.Add(physical[i], i);
                var note = physical[i];
                var policyDeadline = _policy.DeadlineAt(note.Time);
                if (ordinaryWindow is null)
                {
                    _deadlines[i] = policyDeadline;
                    continue;
                }

                var window = ordinaryWindow(note);
                RequireFinite(window.FrontEnd, nameof(window.FrontEnd));
                RequireFinite(window.BackEnd, nameof(window.BackEnd));
                if (window.BackEnd < window.FrontEnd)
                    throw new ArgumentException("The ordinary back end must not precede the front end.", nameof(ordinaryWindow));

                // V1 owns the extended deadline, but never shortens the ordinary engine
                // window. This value is frozen once, so later note-distance changes cannot
                // move a live note's adjudication boundary.
                var ordinaryBackEnd = note.Time + window.BackEnd;
                var graceUntil = Math.Max(policyDeadline.GraceUntil, ordinaryBackEnd);
                _deadlines[i] = new EliteFillDeadline(policyDeadline.AnchorTimestamp,
                    policyDeadline.EntryUntil, graceUntil,
                    graceUntil + _policy.Parameters.PendingCommitDelaySeconds);
            }

            _terminal = new bool[physical.Count];
            _committed = new bool[physical.Count];
            _outcomes = new bool[physical.Count];

            BuildAuthoredLanes(authoredLaneMap, out _noteLanes, out _authoredLanes, out _authoredMembership);

            // Precompute per-lane protection metadata: the distinct member pads and the
            // frozen grace deadline of the lane's final member (the lane's temporal
            // active bound). Both derive from the already-frozen deadline table.
            _laneLastInput = new double[_authoredLanes.Length];
            _laneHasInput = new bool[_authoredLanes.Length];
            _laneEntryTime = new double[_authoredLanes.Length];
            _laneHasEntryTime = new bool[_authoredLanes.Length];
            _lanePads = new int[_authoredLanes.Length][];
            _laneActiveUntil = new double[_authoredLanes.Length];
            for (var laneId = 0; laneId < _authoredLanes.Length; laneId++)
            {
                var lane = _authoredLanes[laneId];
                var pads = new HashSet<int>();
                foreach (var member in lane.Members)
                {
                    pads.Add(member.Pad);
                }

                _lanePads[laneId] = pads.ToArray();
                // Physical grace bounds activity only while the final authored member
                // remains unresolved. Earlier members cannot extend a lane past its end.
                _laneActiveUntil[laneId] = _deadlines[_ordinals[lane.Members[^1]]].GraceUntil;
            }
        }

        /// <summary>
        /// Indexes authored lane membership onto physical notes. Membership claimed by
        /// any authored record — including one whose lane is dropped here because a
        /// member is not part of this journal — suppresses the generic flag fallback for
        /// those notes (fail closed); charts without authored lanes keep the flag-based
        /// fallback for their genuinely metadata-absent notes.
        /// </summary>
        private void BuildAuthoredLanes(EliteFillAuthoredLaneMap? authoredLaneMap,
            out int[][] noteLanes, out EliteFillAuthoredLane[] authoredLaneIndex, out bool[] authoredMembership)
        {
            var noteLaneIds = new List<int>[_physicalNotes.Count];
            for (var i = 0; i < noteLaneIds.Length; i++) noteLaneIds[i] = new List<int>();

            var membership = new bool[_physicalNotes.Count];
            var lanes = new List<EliteFillAuthoredLane>();
            if (authoredLaneMap is not null)
            {
                foreach (var claimed in authoredLaneMap.AuthoredMembershipNotes)
                {
                    if (_ordinals.TryGetValue(claimed, out var claimedOrdinal))
                    {
                        membership[claimedOrdinal] = true;
                    }
                }

                foreach (var lane in authoredLaneMap.Lanes)
                {
                    var valid = true;
                    foreach (var member in lane.Members)
                    {
                        if (_ordinals.ContainsKey(member)) continue;
                        valid = false;
                        break;
                    }

                    if (!valid)
                    {
                        continue;
                    }

                    var laneId = lanes.Count;
                    foreach (var member in lane.Members)
                    {
                        noteLaneIds[_ordinals[member]].Add(laneId);
                    }
                    lanes.Add(lane);
                }
            }

            authoredLaneIndex = lanes.ToArray();
            authoredMembership = membership;
            noteLanes = new int[_physicalNotes.Count][];
            for (var i = 0; i < noteLanes.Length; i++)
            {
                noteLanes[i] = noteLaneIds[i].ToArray();
            }
        }

        public bool Contains(DrumNote note) => note is not null && _ordinals.ContainsKey(note);

        public bool IsTerminal(DrumNote note)
            => note is not null && _ordinals.TryGetValue(note, out var ordinal) && _terminal[ordinal];

        public bool IsCommitted(DrumNote note)
            => note is not null && _ordinals.TryGetValue(note, out var ordinal) && _committed[ordinal];

        public EliteFillDeadline DeadlineAt(DrumNote note)
        {
            if (note is null) throw new ArgumentNullException(nameof(note));
            if (!_ordinals.TryGetValue(note, out var ordinal))
                throw new ArgumentException("The note is not part of this fill journal.", nameof(note));
            return _deadlines[ordinal];
        }

        /// <summary>
        /// Returns an un adjudicated physical note for a real lane input. Kick is a
        /// normal physical lane here; callers deliberately invoke this independently
        /// from hand-lane state so a kick never fabricates hand cadence.
        /// </summary>
        public DrumNote? FindCandidate(int pad, double timestamp,
            Func<DrumNote, (double FrontEnd, double BackEnd)>? ordinaryWindow = null)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            return _physicalNotes
                .Where(note => !IsTerminal(note) && note.Pad == pad)
                .Where(note => IsEligible(note, timestamp, ordinaryWindow))
                .OrderBy(note => _ordinals[note])
                .FirstOrDefault();
        }

        public bool IsEligible(DrumNote note, double timestamp,
            Func<DrumNote, (double FrontEnd, double BackEnd)>? ordinaryWindow = null)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            if (!_ordinals.TryGetValue(note, out var ordinal) || _terminal[ordinal]) return false;

            var deadline = _deadlines[ordinal];
            var frontEnd = deadline.AnchorTimestamp;
            if (ordinaryWindow is not null)
            {
                var window = ordinaryWindow(note);
                frontEnd += window.FrontEnd;
            }

            return timestamp >= frontEnd && timestamp <= deadline.GraceUntil;
        }

        public IEnumerable<double> ExpiryTimes()
        {
            for (var i = _nextCommit; i < _physicalNotes.Count; i++)
            {
                if (!_terminal[i]) yield return _deadlines[i].GraceUntil;
            }
        }

        public bool IsCadenceCovered(DrumNote note)
        {
            if (note is null || !_ordinals.TryGetValue(note, out var ordinal)) return false;

            // Authored notes use the native input-refreshed lane window, scoped to
            // their own phrase identity. A real hit or protected in-lane strike refreshes
            // the timer; automatic note resolution never does. Unresolved/dropped records
            // stay claimed but have no valid lane and therefore fail closed.
            if (_authoredMembership[ordinal])
            {
                if (_terminal[ordinal] || _laneAutohitWindowSeconds <= 0) return false;
                foreach (var laneId in _noteLanes[ordinal])
                {
                    if (!_laneHasInput[laneId] || !IsEnteredLane(laneId)
                        || !AutomaticContinuationAllowed(note, _laneLastInput[laneId])) continue;
                    var expiry = _laneLastInput[laneId] + _laneAutohitWindowSeconds;
                    if (note != _authoredLanes[laneId].StartNote && note.Time <= expiry)
                        return true;
                }
                return false;
            }

            // Fallback for physical notes with no authored metadata at all (legacy,
            // native, and flag-only charts): the generic lane flags are a single-lane
            // approximation. Coverage is authored-lane state, not chart-order commit
            // state. Find the nearest preceding lane start and require that entry to
            // have been hit; a lane end closes the interval, while a different lane
            // cannot refresh it.
            if (!note.IsLane || note.IsLaneStart) return false;
            for (var i = ordinal - 1; i >= 0; i--)
            {
                var prior = _physicalNotes[i];
                if (prior.IsLaneEnd) return false;
                if (!prior.IsLaneStart) continue;
                return _terminal[i] && _outcomes[i];
            }

            return false;
        }

        /// <summary>
        /// Resolves entered-lane cadence members at their authored cadence step. This
        /// is deliberately separate from expiry: a covered member needs no new input,
        /// but an unrelated lane must still wait for its own manual candidate.
        /// </summary>
        public IReadOnlyList<Commit> ResolveCadence(double timestamp)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            var commits = new List<Commit>();
            foreach (var note in _physicalNotes)
            {
                if (!AutomaticContinuationAllowed(note, timestamp) || !IsCadenceCovered(note)
                    || timestamp < note.Time + _policy.Parameters.CadenceStepSeconds)
                    continue;

                if (TryAdjudicate(note, true, timestamp, out var noteCommits))
                    commits.AddRange(noteCommits);
            }

            return commits;
        }

        /// <summary>
        /// Elite V1 authored-lane input protection. Recent input is aggregated across
        /// all valid, entered lanes whose final member is unresolved and temporally
        /// active. Pad eligibility remains lane-local: the struck
        /// pad must belong to at least one such lane. This permits a fast roll across
        /// simultaneous lanes without allowing inactive, ended, unresolved, or
        /// unentered lanes to extend protection. A zero duration safely disables it.
        /// </summary>
        public bool IsAuthoredLaneStrikeProtected(int pad, double timestamp, double protectionWindowSeconds,
            double proximityProtectionWindowSeconds = 0)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            RequireFinite(protectionWindowSeconds, nameof(protectionWindowSeconds));
            RequireFinite(proximityProtectionWindowSeconds, nameof(proximityProtectionWindowSeconds));

            var padBelongsToActiveLane = false;
            var padWithinPostLaneLeniency = false;
            for (var laneId = 0; laneId < _authoredLanes.Length; laneId++)
            {
                if (!IsEnteredLane(laneId)) continue;
                var lane = _authoredLanes[laneId];
                var activeFinalMember = lane.Members[^1];
                var activeFinalOrdinal = _ordinals[activeFinalMember];
                var active = timestamp <= _laneActiveUntil[laneId] && !_terminal[activeFinalOrdinal];

                if (active)
                {
                    padBelongsToActiveLane |= _lanePads[laneId].Contains(pad);
                    // Like native ActiveLaneIncludesNote, accepting an active lane
                    // pad is independent of whether the hit-forgiveness timer lapsed.
                }
                else if (proximityProtectionWindowSeconds > 0 && _lanePads[laneId].Contains(pad))
                {
                    var finalMember = lane.Members[^1];
                    var finalOrdinal = _ordinals[finalMember];
                    padWithinPostLaneLeniency |= _terminal[finalOrdinal] && _outcomes[finalOrdinal]
                        && timestamp - finalMember.Time < proximityProtectionWindowSeconds;
                }
            }

            return padBelongsToActiveLane || padWithinPostLaneLeniency;
        }

        private bool IsEnteredLane(int laneId)
        {
            var lane = _authoredLanes[laneId];
            return _ordinals.TryGetValue(lane.StartNote, out var startOrdinal)
                && _terminal[startOrdinal] && _outcomes[startOrdinal];
        }

        private bool IsEnteredLaneActive(int laneId, double timestamp)
        {
            if (timestamp > _laneActiveUntil[laneId] || !IsEnteredLane(laneId)) return false;
            var finalMember = _authoredLanes[laneId].Members[^1];
            return !_terminal[_ordinals[finalMember]];
        }

        /// <summary>
        /// Records a qualifying real input for every authored lane containing the hit
        /// physical note. This is the authored-lane analog of the native
        /// SubmitLaneNote/UpdateLaneAutohitExpireTime refresh: only the matching lanes
        /// are refreshed, so one input can never manufacture cadence for an unrelated
        /// or merely overlapping lane.
        /// </summary>
        public void RecordAuthoredLaneInput(DrumNote hitNote, double timestamp)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            if (hitNote is null || !_ordinals.TryGetValue(hitNote, out var ordinal)) return;

            foreach (var laneId in _noteLanes[ordinal])
            {
                if (hitNote == _authoredLanes[laneId].StartNote && !_laneHasEntryTime[laneId])
                {
                    _laneEntryTime[laneId] = timestamp;
                    _laneHasEntryTime[laneId] = true;
                }

                if (!_laneHasInput[laneId] || timestamp > _laneLastInput[laneId])
                {
                    _laneLastInput[laneId] = timestamp;
                    _laneHasInput[laneId] = true;
                }
            }
        }

        /// <summary>
        /// Refreshes every entered authored lane whose membership includes the struck
        /// pad, mirroring native lane input acceptance: only the lane the input
        /// actually satisfies is extended.
        /// </summary>
        public void RecordAuthoredLanePadInput(int pad, double timestamp)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            for (var laneId = 0; laneId < _authoredLanes.Length; laneId++)
            {
                if (!_lanePads[laneId].Contains(pad) || !IsEnteredLaneActive(laneId, timestamp)) continue;

                if (!_laneHasInput[laneId] || timestamp > _laneLastInput[laneId])
                {
                    _laneLastInput[laneId] = timestamp;
                    _laneHasInput[laneId] = true;
                }
            }
        }

        /// <summary>Marks an off-note barrier in the live V1 state machine.</summary>
        public void LatchBarrier(double timestamp)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            _barrier = EliteFillBarrierCalculator.Calculate(timestamp, _policy.Parameters);
        }

        /// <summary>Marks one physical note terminal. A note is never adjudicated twice.</summary>
        public bool TryAdjudicate(DrumNote note, bool wasHit, double timestamp,
            out IReadOnlyList<Commit> commits)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            if (note is null || !_ordinals.TryGetValue(note, out var ordinal) || _terminal[ordinal])
            {
                commits = Array.Empty<Commit>();
                return false;
            }

            _terminal[ordinal] = true;
            _outcomes[ordinal] = wasHit;
            TerminalCount++;
            var laneIds = _noteLanes[ordinal].Length == 0 ? "none" : string.Join(",", _noteLanes[ordinal]);
            DrumsEngine.TraceEliteLane($"adjudicate outcome={(wasHit ? "hit" : "miss")} tick={note.Tick} pad={note.Pad} " +
                $"time={timestamp:F6} origin={note.ConversionOrigin?.ToString() ?? "none"} " +
                $"flags={note.Flags} drumFlags={note.DrumFlags} authored={_authoredMembership[ordinal]} lanes={laneIds}");

            commits = ReleaseCommits();
            return true;
        }

        /// <summary>
        /// Misses notes only after the exact frozen V1 grace deadline. Equality is still
        /// inside the grace window; callers schedule/use the next representable value.
        /// Entered hand lanes auto-resolve cadence-covered gems without requiring a new
        /// physical input, while kicks always remain manual physical notes.
        /// </summary>
        public IReadOnlyList<Commit> Expire(double timestamp)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            for (var i = _nextCommit; i < _physicalNotes.Count; i++)
            {
                if (_terminal[i]) continue;
                var note = _physicalNotes[i];
                if (timestamp <= _deadlines[i].GraceUntil) continue;
                // Evaluate coverage while this note is still unresolved: authored
                // coverage deliberately rejects terminal notes. Marking it terminal
                // first would turn every covered note into an expiry miss.
                var covered = AutomaticContinuationAllowed(note, timestamp) && IsCadenceCovered(note);
                _terminal[i] = true;
                // A barrier suppresses the automatic cadence/autohit outcome, but it
                // must not suppress ordinary expiry/miss progression or strand later
                // real inputs behind an unresolved earlier physical note.
                _outcomes[i] = covered;
                TerminalCount++;
            }

            return ReleaseCommits();
        }

        /// <summary>Flushes unresolved notes strictly before a Coda boundary.</summary>
        public IReadOnlyList<Commit> FlushBefore(double timestamp)
        {
            EliteFillParameterValidation.RequireTimestamp(timestamp);
            for (var i = _nextCommit; i < _physicalNotes.Count; i++)
            {
                var note = _physicalNotes[i];
                if (_terminal[i] || note.Time >= timestamp) continue;
                _terminal[i] = true;
                _outcomes[i] = false;
                TerminalCount++;
            }

            return ReleaseCommits();
        }

        public void Reset()
        {
            Array.Clear(_terminal, 0, _terminal.Length);
            Array.Clear(_committed, 0, _committed.Length);
            Array.Clear(_outcomes, 0, _outcomes.Length);
            _barrier = null;
            TerminalCount = 0;
            CommittedCount = 0;
            _nextCommit = 0;
            Array.Clear(_laneLastInput, 0, _laneLastInput.Length);
            Array.Clear(_laneHasInput, 0, _laneHasInput.Length);
            Array.Clear(_laneEntryTime, 0, _laneEntryTime.Length);
            Array.Clear(_laneHasEntryTime, 0, _laneHasEntryTime.Length);
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, value, "Must be finite.");
        }

        private IReadOnlyList<Commit> ReleaseCommits()
        {
            if (_nextCommit >= _physicalNotes.Count) return Array.Empty<Commit>();

            var commits = new List<Commit>();
            while (_nextCommit < _physicalNotes.Count && _terminal[_nextCommit])
            {
                _committed[_nextCommit] = true;
                commits.Add(new Commit(_physicalNotes[_nextCommit], _outcomes[_nextCommit]));
                _nextCommit++;
                CommittedCount++;
            }

            return commits;
        }
    }
}
