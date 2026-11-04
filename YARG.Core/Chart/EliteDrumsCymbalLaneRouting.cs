// pattern: Functional Core
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace YARG.Core.Chart
{
    /// <summary>Immutable full-song lane reservations shared by owned playback clones and practice slices.</summary>
    public sealed class EliteDrumsCymbalLaneRouting
    {
        internal string PairChoice { get; }
        internal byte Policy { get; }
        internal IReadOnlyDictionary<EliteDrumSourceDefinition, int> Targets { get; }

        internal EliteDrumsCymbalLaneRouting(string pairChoice, byte policy,
            IDictionary<EliteDrumSourceDefinition, int> targets)
        {
            PairChoice = pairChoice;
            Policy = policy;
            Targets = new ReadOnlyDictionary<EliteDrumSourceDefinition, int>(
                new Dictionary<EliteDrumSourceDefinition, int>(targets));
        }
    }
}
