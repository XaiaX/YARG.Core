// pattern: Functional Core
using YARG.Core.Input;

namespace YARG.Core.Engine.Drums
{
    public enum HiHatClosureDisposition { Forgiven, Stomp, Cymbal, Both, OpenHatPedalFault }

    /// <summary>Mapped input evidence for a future diagnostic subscriber. Produces no logging itself.</summary>
    public readonly struct HiHatClosureObservation
    {
        public readonly GameInput PedalInput;
        public readonly GameInput ClosedHatInput;
        public readonly HiHatClosureDisposition Disposition;
        public double TimeDelta => ClosedHatInput.Time - PedalInput.Time;
        /// <summary>Null for historical boolean presses. MIDI notes carry velocity; CC carries position.
        /// Mapped inputs do not identify their source, so this is not necessarily measured velocity.</summary>
        public float? PedalValue => PedalInput.Integer == 1 ? null : PedalInput.Axis;
        /// <summary>MIDI note bindings carry velocity; other mapped controls may carry a synthetic or CC value.</summary>
        public float ClosedHatValue => ClosedHatInput.Axis;

        public HiHatClosureObservation(GameInput pedalInput, GameInput closedHatInput,
            HiHatClosureDisposition disposition)
        {
            PedalInput = pedalInput;
            ClosedHatInput = closedHatInput;
            Disposition = disposition;
        }
    }
}
