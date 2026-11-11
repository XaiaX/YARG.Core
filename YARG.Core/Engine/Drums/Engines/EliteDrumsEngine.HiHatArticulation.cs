// pattern: Functional Core
using YARG.Core.Chart;
using YARG.Core.Input;

namespace YARG.Core.Engine.Drums.Engines
{
    public partial class EliteDrumsEngine
    {
        /// <summary>True during the hit callback for a hi-hat strike scored at reduced credit.</summary>
        public bool IsJudgingMisarticulatedHiHat { get; private set; }

        /// <summary>True during the hit callback when a closed strike forfeited an open note's sustain.</summary>
        public bool IsJudgingClosedAsOpen { get; private set; }

        private int HiHatArticulationDivisor(EliteDrumsAction action, EliteDrumNote note)
        {
            if (!EngineParameters.LenientHiHatArticulation ||
                note.Pad != (int)EliteDrumNote.EliteDrumPad.HiHat || note.IsIndifferent) return 1;
            if (action == EliteDrumsAction.EliteSizzleHiHat) return 2;
            if (note.IsOpen && action == EliteDrumsAction.EliteClosedHiHat ||
                note.IsClosed && action == EliteDrumsAction.EliteOpenHiHat) return 4;
            return 1;
        }
    }
}
