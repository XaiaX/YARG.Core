using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Core.Chart
{
    /// <summary>
    /// One native Elite Drums authored pad-lane phrase, identified by its position in the loaded
    /// difficulty's phrase list. Membership refers to authored gems, not inferred pad flags.
    /// The interval is half-open; slicing retains the original phrase identity.
    /// </summary>
    public sealed class EliteDrumNativeAuthoredLaneRecord
    {
        private readonly IReadOnlyList<EliteDrumSourceDefinition> _memberSources;

        public int PhraseOrdinal { get; }
        public PhraseType LaneType { get; }
        public EliteDrumNote.EliteDrumPad AuthoredPad { get; }
        public uint StartTick { get; }
        public uint EndTick { get; }
        public IReadOnlyList<EliteDrumSourceDefinition> MemberSources => _memberSources;

        public EliteDrumNativeAuthoredLaneRecord(int phraseOrdinal, PhraseType laneType,
            uint startTick, uint endTick, IEnumerable<EliteDrumSourceDefinition> memberSources)
        {
            if (phraseOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(phraseOrdinal));
            if (!TryGetNativePad(laneType, out var pad))
                throw new ArgumentOutOfRangeException(nameof(laneType));
            if (endTick < startTick) throw new ArgumentOutOfRangeException(nameof(endTick));
            PhraseOrdinal = phraseOrdinal;
            LaneType = laneType;
            AuthoredPad = pad;
            StartTick = startTick;
            EndTick = endTick;
            _memberSources = Array.AsReadOnly((memberSources ?? throw new ArgumentNullException(nameof(memberSources)))
                .Distinct().ToArray());
        }

        public static bool TryGetNativePad(PhraseType type, out EliteDrumNote.EliteDrumPad pad)
        {
            pad = type switch
            {
                PhraseType.EliteDrums_HatPedalLane => EliteDrumNote.EliteDrumPad.HatPedal,
                PhraseType.EliteDrums_KickLane => EliteDrumNote.EliteDrumPad.Kick,
                PhraseType.EliteDrums_SnareLane => EliteDrumNote.EliteDrumPad.Snare,
                PhraseType.EliteDrums_HiHatLane => EliteDrumNote.EliteDrumPad.HiHat,
                PhraseType.EliteDrums_LeftCrashLane => EliteDrumNote.EliteDrumPad.LeftCrash,
                PhraseType.EliteDrums_Tom1Lane => EliteDrumNote.EliteDrumPad.Tom1,
                PhraseType.EliteDrums_Tom2Lane => EliteDrumNote.EliteDrumPad.Tom2,
                PhraseType.EliteDrums_Tom3Lane => EliteDrumNote.EliteDrumPad.Tom3,
                PhraseType.EliteDrums_RideLane => EliteDrumNote.EliteDrumPad.Ride,
                PhraseType.EliteDrums_RightCrashLane => EliteDrumNote.EliteDrumPad.RightCrash,
                _ => default
            };
            return type is >= PhraseType.EliteDrums_RightCrashLane and <= PhraseType.EliteDrums_HatPedalLane;
        }

        /// <summary>Clip to a half-open practice window without changing the authored phrase identity.</summary>
        public EliteDrumNativeAuthoredLaneRecord? Slice(uint startTick, uint endTick)
        {
            if (endTick < startTick) throw new ArgumentOutOfRangeException(nameof(endTick));
            uint start = Math.Max(StartTick, startTick);
            uint end = Math.Min(EndTick, endTick);
            return start < end ? new EliteDrumNativeAuthoredLaneRecord(PhraseOrdinal, LaneType,
                start, end, _memberSources.Where(source => source.StartTick >= start && source.StartTick < end)) : null;
        }
    }
}
