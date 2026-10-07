using System;
using YARG.Core.Chart;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.Game
{
    /// <summary>Native Elite note identities, independent of their shared visual positions.</summary>
    public enum EliteDrumsColorRole
    {
        Snare, HatIndifferent, HatOpen, HatClosed, LeftCrash, Ride, RightCrash,
        Tom1, Tom2, Tom3, Kick, DoubleKick, KickFlam, Stomp, Splash, HandFlam, Wildcard
    }

    public static class EliteDrumsColorRoles
    {
        public static EliteDrumsColorRole GetInputRole(EliteDrumsAction action) => action switch
        {
            EliteDrumsAction.Kick => EliteDrumsColorRole.Kick,
            EliteDrumsAction.EliteStomp => EliteDrumsColorRole.Stomp,
            EliteDrumsAction.EliteSplash => EliteDrumsColorRole.Splash,
            EliteDrumsAction.EliteSnare => EliteDrumsColorRole.Snare,
            EliteDrumsAction.EliteClosedHiHat => EliteDrumsColorRole.HatClosed,
            EliteDrumsAction.EliteOpenHiHat => EliteDrumsColorRole.HatOpen,
            EliteDrumsAction.EliteSizzleHiHat => EliteDrumsColorRole.HatIndifferent,
            EliteDrumsAction.EliteLeftCrash => EliteDrumsColorRole.LeftCrash,
            EliteDrumsAction.EliteRide => EliteDrumsColorRole.Ride,
            EliteDrumsAction.EliteRightCrash => EliteDrumsColorRole.RightCrash,
            EliteDrumsAction.EliteTom1 => EliteDrumsColorRole.Tom1,
            EliteDrumsAction.EliteTom2 => EliteDrumsColorRole.Tom2,
            EliteDrumsAction.EliteTom3 => EliteDrumsColorRole.Tom3,
            EliteDrumsAction.FourLaneRedDrum or EliteDrumsAction.FiveLaneRedDrum => EliteDrumsColorRole.Snare,
            EliteDrumsAction.FourLaneYellowDrum or EliteDrumsAction.FourLaneYellowCymbal or
                EliteDrumsAction.FiveLaneYellowCymbal => EliteDrumsColorRole.HatIndifferent,
            EliteDrumsAction.FourLaneBlueDrum or EliteDrumsAction.FourLaneBlueCymbal or
                EliteDrumsAction.FiveLaneBlueDrum => EliteDrumsColorRole.Ride,
            EliteDrumsAction.FourLaneGreenDrum or EliteDrumsAction.FourLaneGreenCymbal or
                EliteDrumsAction.FiveLaneGreenDrum => EliteDrumsColorRole.RightCrash,
            EliteDrumsAction.FiveLaneOrangeCymbal => EliteDrumsColorRole.LeftCrash,
            EliteDrumsAction.WildcardPad => EliteDrumsColorRole.Snare,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Not a native Elite input.")
        };

        /// <summary>Split hand flams keep each hand's own role; unsplit hand flams use HandFlam.</summary>
        public static EliteDrumsColorRole GetRole(EliteDrumNote note, bool splitHandFlam = false)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            if (note.IsInvisibleTerminator && note.Pad == (int) EliteDrumPad.HatPedal)
                throw new ArgumentException("Invisible hat terminators have no playable color role.", nameof(note));

            if (note.Pad == (int) EliteDrumPad.Kick)
                return note.IsFlam || note.IsFlatFlam ? EliteDrumsColorRole.KickFlam :
                    note.IsDoubleKick ? EliteDrumsColorRole.DoubleKick : EliteDrumsColorRole.Kick;
            if (note.Pad == (int) EliteDrumPad.HatPedal)
                return note.IsSplash ? EliteDrumsColorRole.Splash : EliteDrumsColorRole.Stomp;
            if ((note.IsFlam || note.IsFlatFlam) && !splitHandFlam) return EliteDrumsColorRole.HandFlam;

            return (EliteDrumPad) note.Pad switch
            {
                EliteDrumPad.Wildcard => EliteDrumsColorRole.Wildcard,
                EliteDrumPad.Snare => EliteDrumsColorRole.Snare,
                EliteDrumPad.HiHat => note.HatState switch
                {
                    EliteDrumsHatState.Open => EliteDrumsColorRole.HatOpen,
                    EliteDrumsHatState.Closed => EliteDrumsColorRole.HatClosed,
                    _ => EliteDrumsColorRole.HatIndifferent
                },
                EliteDrumPad.LeftCrash => EliteDrumsColorRole.LeftCrash,
                EliteDrumPad.Ride => EliteDrumsColorRole.Ride,
                EliteDrumPad.RightCrash => EliteDrumsColorRole.RightCrash,
                EliteDrumPad.Tom1 => EliteDrumsColorRole.Tom1,
                EliteDrumPad.Tom2 => EliteDrumsColorRole.Tom2,
                EliteDrumPad.Tom3 => EliteDrumsColorRole.Tom3,
                _ => throw new ArgumentOutOfRangeException(nameof(note), note.Pad, "Unknown Elite pad.")
            };
        }
    }
}
