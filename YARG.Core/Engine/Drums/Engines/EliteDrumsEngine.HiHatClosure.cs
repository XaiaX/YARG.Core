// pattern: Functional Core
using System;
using System.Collections.Generic;
using YARG.Core.Chart;
using YARG.Core.Input;

namespace YARG.Core.Engine.Drums.Engines
{
    public partial class EliteDrumsEngine
    {
        private enum ClosureDecision { Normal, Ignore, PedalFault }
        private readonly Dictionary<long, ClosureDecision> _closureCompanions = new();
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
            _closureInputSequence = 0;
            _closureDecision = ClosureDecision.Normal;
            _ignoredClosureInput = null;
        }

        private static bool IsClosureInput(GameInput input)
        {
            var action = input.GetAction<EliteDrumsAction>();
            if (action is not (EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteClosedHiHat)) return false;
            if (action == EliteDrumsAction.EliteStomp && input.Integer == 1) return true;
            return input.Integer != 1 && input.Axis > 0 && !float.IsNaN(input.Axis) && !float.IsInfinity(input.Axis);
        }

        private ClosureDecision ResolveHiHatClosure(GameInput input)
        {
            if (!EngineParameters.HiHatClosureSuppression || IsBot) return ClosureDecision.Normal;
            long sequence = _closureInputSequence++;
            if (_closureCompanions.TryGetValue(sequence, out var reserved))
            {
                _closureCompanions.Remove(sequence);
                return reserved;
            }
            if (!IsClosureInput(input)) return ClosureDecision.Normal;
            long companionSequence = sequence;
            foreach (var companion in InputQueue)
            {
                companionSequence++;
                if (companion.Time > input.Time + InputProcessingDelay) break;
                if (_closureCompanions.ContainsKey(companionSequence) || !IsClosureInput(companion) ||
                    companion.Action == input.Action) continue;
                bool firstIsPedal = input.GetAction<EliteDrumsAction>() == EliteDrumsAction.EliteStomp;
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
                    if (Matches(EliteDrumsAction.EliteClosedHiHat, note)) cymbal = true;
                    if (note.Pad == (int)EliteDrumNote.EliteDrumPad.HiHat && note.IsOpen) open = true;
                }
            }
            // A valid second flam strike is still a cymbal target after its head was hit.
            if (_pendingFlams.TryGetValue((int)EliteDrumNote.EliteDrumPad.HiHat, out var flam) &&
                closedTime >= flam.Start && closedTime <= flam.End &&
                Matches(EliteDrumsAction.EliteClosedHiHat, flam.Note)) cymbal = true;
            if (stomp) return cymbal ? HiHatClosureDisposition.Both : HiHatClosureDisposition.Stomp;
            if (cymbal) return HiHatClosureDisposition.Cymbal;
            return open || WouldBreakHiHatSustain(pedalTime)
                ? HiHatClosureDisposition.OpenHatPedalFault : HiHatClosureDisposition.Forgiven;
        }
    }
}
