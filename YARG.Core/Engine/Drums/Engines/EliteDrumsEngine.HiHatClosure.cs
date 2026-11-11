// pattern: Functional Core
using System;
using System.Collections.Generic;
using YARG.Core.Chart;
using YARG.Core.Input;

namespace YARG.Core.Engine.Drums.Engines
{
    public partial class EliteDrumsEngine
    {
        private enum ClosureDecision { Normal, Ignore, PedalFault, Duplicate }
        private readonly Dictionary<long, ClosureDecision> _closureCompanions = new();
        private readonly HashSet<long> _pedalsWithDuplicate = new();
        private long _closureInputSequence;
        private ClosureDecision _closureDecision;
        private GameInput? _ignoredClosureInput;

        public Action<HiHatClosureObservation>? OnHiHatClosurePair;
        /// <summary>True while the paired open-hat fault's feedback and overhit callbacks execute.</summary>
        public bool IsHiHatClosurePedalOverhit => _closureDecision == ClosureDecision.PedalFault;
        public override double InputProcessingDelay => !IsBot && EngineParameters.HiHatClosureSuppression
            ? EngineParameters.HiHatClosureWindow * (EngineParameters.SongSpeed > 0 ? EngineParameters.SongSpeed : 1)
            : 0;

        private void ResetHiHatClosures()
        {
            _closureCompanions?.Clear();
            _pedalsWithDuplicate?.Clear();
            _closureInputSequence = 0;
            _closureDecision = ClosureDecision.Normal;
            _ignoredClosureInput = null;
        }

        private static bool IsClosureInput(GameInput input)
        {
            var action = input.GetAction<EliteDrumsAction>();
            if (action == EliteDrumsAction.EliteHiHatPosition) return input.Integer == 1;
            if (action is not (EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteClosedHiHat)) return false;
            if (action == EliteDrumsAction.EliteStomp && input.Integer == 1) return true;
            return input.Integer != 1 && input.Axis > 0 && !float.IsNaN(input.Axis) && !float.IsInfinity(input.Axis);
        }

        private static bool IsPedalClosure(GameInput input) => IsClosureInput(input) &&
            input.GetAction<EliteDrumsAction>() != EliteDrumsAction.EliteClosedHiHat;

        private bool ReserveDuplicatePedal(GameInput pedal, long pedalSequence, long currentSequence,
            double availableUntil)
        {
            var otherSource = pedal.GetAction<EliteDrumsAction>() == EliteDrumsAction.EliteStomp
                ? EliteDrumsAction.EliteHiHatPosition : EliteDrumsAction.EliteStomp;
            long sequence = currentSequence;
            foreach (var candidate in InputQueue)
            {
                sequence++;
                if (candidate.Time > availableUntil) break;
                if (sequence <= pedalSequence || candidate.GetAction<EliteDrumsAction>() != otherSource ||
                    !IsClosureInput(candidate) || _closureCompanions.ContainsKey(sequence))
                    continue;

                _closureCompanions.Add(sequence, ClosureDecision.Duplicate);
                return true;
            }
            return false;
        }

        private ClosureDecision ResolveHiHatClosure(GameInput input)
        {
            if (!EngineParameters.HiHatClosureSuppression || IsBot) return ClosureDecision.Normal;
            long sequence = _closureInputSequence++;
            _closureCompanions.Remove(sequence, out var reserved);
            if (reserved == ClosureDecision.Duplicate || !IsClosureInput(input)) return reserved;

            double availableUntil = input.Time + InputProcessingDelay;
            bool firstIsPedal = IsPedalClosure(input);
            if (firstIsPedal && !_pedalsWithDuplicate.Remove(sequence))
                ReserveDuplicatePedal(input, sequence, sequence, availableUntil);
            if (reserved != ClosureDecision.Normal) return reserved;

            long companionSequence = sequence;
            foreach (var companion in InputQueue)
            {
                companionSequence++;
                if (companion.Time > availableUntil) break;
                if (!IsClosureInput(companion) || IsPedalClosure(companion) == firstIsPedal) continue;
                bool alreadyReserved = _closureCompanions.TryGetValue(companionSequence, out var decision);
                if (decision == ClosureDecision.Duplicate) continue;
                // A second strike must skip the redundant pedal source even if the first
                // strike reserved the primary before the redundant event arrived.
                if (!firstIsPedal && !_pedalsWithDuplicate.Contains(companionSequence) &&
                    ReserveDuplicatePedal(companion, companionSequence, sequence, availableUntil))
                    _pedalsWithDuplicate.Add(companionSequence);
                if (alreadyReserved) continue;

                var pedal = firstIsPedal ? input : companion;
                var closed = firstIsPedal ? companion : input;
                var disposition = ClassifyHiHatClosure(pedal.Time, closed.Time);
                var pedalDecision = disposition switch {
                    HiHatClosureDisposition.Stomp or HiHatClosureDisposition.Both => ClosureDecision.Normal,
                    HiHatClosureDisposition.OpenHatPedalFault => ClosureDecision.PedalFault,
                    _ => ClosureDecision.Ignore
                };
                var cymbalDecision = disposition is HiHatClosureDisposition.Cymbal or HiHatClosureDisposition.Both
                    ? ClosureDecision.Normal : ClosureDecision.Ignore;
                _closureCompanions.Add(companionSequence, firstIsPedal ? cymbalDecision : pedalDecision);
                OnHiHatClosurePair?.Invoke(new HiHatClosureObservation(pedal, closed, disposition));
                return firstIsPedal ? pedalDecision : cymbalDecision;
            }
            return ClosureDecision.Normal;
        }

        private HiHatClosureDisposition ClassifyHiHatClosure(double pedalTime, double closedTime)
        {
            bool stomp = false, cymbal = false, open = false;
            var window = EngineParameters.HitWindow;
            double latestOnset = Math.Max(pedalTime, closedTime) - window.GetFrontEnd(window.MaxWindow);
            for (int i = NoteIndex; i < Notes.Count; i++)
            {
                if (Notes[i].Time > latestOnset) break;
                foreach (var note in Notes[i].AllNotes)
                {
                    if (note.WasHit || note.WasMissed || note.IsInvisibleTerminator) continue;
                    if (note.Pad == (int)EliteDrumNote.EliteDrumPad.HatPedal && note.IsStomp &&
                        IsNoteInWindow(note, out _, pedalTime)) stomp = true;
                    if (!IsNoteInWindow(note, out _, closedTime)) continue;
                    if (Matches(EliteDrumsAction.EliteClosedHiHat, note) ||
                        HiHatArticulationDivisor(EliteDrumsAction.EliteClosedHiHat, note) > 1) cymbal = true;
                    if (note.Pad == (int)EliteDrumNote.EliteDrumPad.HiHat && note.IsOpen) open = true;
                }
            }
            // A valid second flam strike is still a cymbal target after its head was hit.
            if (_pendingFlams.TryGetValue((int)EliteDrumNote.EliteDrumPad.HiHat, out var flam) &&
                closedTime >= flam.Start && closedTime <= flam.End &&
                (Matches(EliteDrumsAction.EliteClosedHiHat, flam.Note) ||
                    HiHatArticulationDivisor(EliteDrumsAction.EliteClosedHiHat, flam.Note) > 1)) cymbal = true;
            if (stomp) return cymbal ? HiHatClosureDisposition.Both : HiHatClosureDisposition.Stomp;
            if (cymbal) return HiHatClosureDisposition.Cymbal;
            return open || WouldBreakHiHatSustain(pedalTime)
                ? HiHatClosureDisposition.OpenHatPedalFault : HiHatClosureDisposition.Forgiven;
        }
    }
}
