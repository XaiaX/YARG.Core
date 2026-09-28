using System;
using System.Collections.Generic;
using YARG.Core.Chart;

namespace YARG.Core.Engine.Drums
{
    /// <summary>Resolved native source membership; never consults generic note lane flags.</summary>
    internal sealed class NativeEliteAuthoredLaneMap
    {
        internal sealed class Lane
        {
            public readonly EliteDrumNote[] Members;
            public readonly int Pad;
            public double LastRefresh = double.NegativeInfinity;
            public double EntryTime = double.NegativeInfinity;
            public bool Entered;

            public Lane(EliteDrumNote[] members, int pad)
            {
                Members = members;
                Pad = pad;
            }
        }

        private readonly Dictionary<EliteDrumNote, List<Lane>> _members = new();
        private readonly List<Lane> _lanes = new();
        private EliteFillBarrierWindow? _barrier;
        public NativeEliteAuthoredLaneMap(InstrumentDifficulty<EliteDrumNote> chart)
        {
            var sourceNotes = new Dictionary<EliteDrumSourceDefinition, EliteDrumNote>();
            foreach (var parent in chart.Notes)
                foreach (var note in parent.AllNotes)
                    if (!note.IsInvisibleTerminator && note.SourceDefinition is { } source)
                        sourceNotes.TryAdd(source, note);

            foreach (var record in chart.EliteDrumNativeAuthoredLaneRecords)
            {
                // A missing source is valid only if the gameplay-only No Pedal transform
                // intentionally removed that pad. Other dropped identities fail closed.
                var members = new List<EliteDrumNote>();
                bool malformed = false;
                foreach (var source in record.MemberSources)
                {
                    if (source is null || source.Pad != (int) record.AuthoredPad ||
                        source.StartTick < record.StartTick || source.StartTick >= record.EndTick)
                    {
                        malformed = true;
                        break;
                    }
                    if (!sourceNotes.TryGetValue(source, out var note))
                    {
                        if (!chart.NativeElitePedalsFiltered ||
                            record.AuthoredPad != EliteDrumNote.EliteDrumPad.HatPedal)
                            malformed = true;
                        continue;
                    }
                    if (!members.Contains(note)) members.Add(note);
                }
                if (malformed || members.Count < 2) continue;
                members.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Tick.CompareTo(b.Tick));
                var lane = new Lane(members.ToArray(), (int) record.AuthoredPad);
                _lanes.Add(lane);
                foreach (var note in members)
                {
                    if (!_members.TryGetValue(note, out var lanes)) _members[note] = lanes = new List<Lane>();
                    lanes.Add(lane);
                }
            }
        }

        public void Reset()
        {
            foreach (var lane in _lanes)
            {
                lane.Entered = false;
                lane.LastRefresh = double.NegativeInfinity;
                lane.EntryTime = double.NegativeInfinity;
            }
            _barrier = null;
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
                if (lane.Entered) lane.LastRefresh = time;
            }
        }

        public bool ProtectStrike(int pad, double time, Func<EliteDrumNote, bool> matchesAction)
        {
            bool protectedStrike = false;
            foreach (var lane in _lanes)
            {
                if (!lane.Entered || lane.Pad != pad || lane.Members[^1].WasHit ||
                    lane.Members[^1].WasMissed || time < lane.EntryTime ||
                    time > EliteFillPolicyV1.Default.DeadlineAt(lane.Members[^1].Time).GraceUntil ||
                    time > EliteFillPolicyV1.Default.DeadlineAt(lane.LastRefresh).GraceUntil ||
                    !matchesAction(lane.Members[0]))
                    continue;
                if (_barrier is { } barrier && barrier.At(time) != EliteFillBarrierPhase.Clear &&
                    lane.EntryTime < barrier.TriggerTimestamp) continue;
                lane.LastRefresh = time;
                protectedStrike = true;
            }
            return protectedStrike;
        }

        public void Miss(EliteDrumNote note)
        {
            if (!_members.TryGetValue(note, out var lanes)) return;
            foreach (var lane in lanes)
            {
                lane.Entered = false;
                lane.LastRefresh = double.NegativeInfinity;
            }
        }

        public bool CanContinue(EliteDrumNote note, double judgmentTime)
        {
            // Authored kick membership protects real pedal strikes; it never
            // manufactures a kick (including an unplayed Expert+ 2x gem).
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.Kick ||
                !_members.TryGetValue(note, out var lanes)) return false;
            foreach (var lane in lanes)
            {
                if (ReferenceEquals(lane.Members[0], note) || !lane.Entered ||
                    lane.Members[^1].WasHit || lane.Members[^1].WasMissed) continue;
                if (_barrier is { } barrier && barrier.At(judgmentTime) != EliteFillBarrierPhase.Clear &&
                    lane.EntryTime < barrier.TriggerTimestamp) continue;
                var deadline = EliteFillPolicyV1.Default.DeadlineAt(lane.LastRefresh);
                if (note.Time <= deadline.GraceUntil && judgmentTime <= deadline.GraceUntil)
                    return true;
            }
            return false;
        }

        public double? NextContinuationDeadline(EliteDrumNote note)
        {
            if (note.Pad == (int) EliteDrumNote.EliteDrumPad.Kick ||
                !_members.TryGetValue(note, out var lanes)) return null;
            double? earliest = null;
            foreach (var lane in lanes)
            {
                if (ReferenceEquals(lane.Members[0], note) || !lane.Entered ||
                    lane.Members[^1].WasHit || lane.Members[^1].WasMissed) continue;
                var grace = EliteFillPolicyV1.Default.DeadlineAt(lane.LastRefresh).GraceUntil;
                if (note.Time > grace) continue;
                double deadline = Math.Min(note.Time + EliteFillPolicyV1.Default.Parameters.CadenceStepSeconds,
                    grace);
                if (earliest is null || deadline < earliest) earliest = deadline;
            }
            return earliest;
        }
    }
}
