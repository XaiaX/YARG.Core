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

        private EliteFillBarrierWindow? _barrier;
        private int _nextCommit;

        public IReadOnlyList<DrumNote> PhysicalNotes => _physicalNotes;
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

        public EliteFillRuntimeScheduler(IEnumerable<DrumNote> chartNotes, EliteFillPolicyV1 policy,
            Func<DrumNote, (double FrontEnd, double BackEnd)>? ordinaryWindow = null)
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
                .OrderBy(note => Math.Abs(note.Time - timestamp))
                .ThenBy(note => _ordinals[note])
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
            if (note is null || !note.IsLane || note.IsLaneStart) return false;
            if (!_ordinals.TryGetValue(note, out var ordinal)) return false;

            // Coverage is authored-lane state, not chart-order commit state. Find the
            // nearest preceding lane start and require that entry to have been hit;
            // a lane end closes the interval, while a different lane cannot refresh it.
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
            if (!AutomaticContinuationAllowed(timestamp)) return Array.Empty<Commit>();
            var commits = new List<Commit>();
            foreach (var note in _physicalNotes)
            {
                if (!IsCadenceCovered(note) || timestamp < note.Time + _policy.Parameters.CadenceStepSeconds)
                    continue;

                if (TryAdjudicate(note, true, timestamp, out var noteCommits))
                    commits.AddRange(noteCommits);
            }

            return commits;
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
            var automaticContinuationAllowed = AutomaticContinuationAllowed(timestamp);
            for (var i = _nextCommit; i < _physicalNotes.Count; i++)
            {
                if (_terminal[i]) continue;
                var note = _physicalNotes[i];
                if (timestamp <= _deadlines[i].GraceUntil) continue;
                _terminal[i] = true;
                // A barrier suppresses the automatic cadence/autohit outcome, but it
                // must not suppress ordinary expiry/miss progression or strand later
                // real inputs behind an unresolved earlier physical note.
                _outcomes[i] = automaticContinuationAllowed && IsCadenceCovered(note);
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
