using System;
using System.IO;
using YARG.Core.Chart;

namespace YARG.Core.Game
{
    /// <summary>
    /// Selects the drum track recorded by a profile. Native selection remains unchanged when
    /// no explicit Elite downchart target is present; an explicit target is never silently
    /// replaced by native content.
    /// </summary>
    public static class DrumDifficultySelector
    {
        /// <summary>
        /// Selects the native drum track or the generated Elite downchart requested by
        /// <paramref name="profile"/>.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// Thrown when an explicit target is malformed, inconsistent with the profile, or
        /// was not generated on the supplied chart.
        /// </exception>
        public static InstrumentTrack<DrumNote> SelectTrack(SongChart chart, YargProfile profile)
        {
            if (chart is null) throw new ArgumentNullException(nameof(chart));
            if (profile is null) throw new ArgumentNullException(nameof(profile));

            var target = profile.EliteDrumsDownchartTarget;
            if (target is null)
            {
                return chart.GetDrumsTrack(profile.CurrentInstrument);
            }

            if (!EliteDrumsDownchartRules.IsValidTarget(target.Value) ||
                profile.GameMode is not (GameMode.FourLaneDrums or GameMode.FiveLaneDrums or GameMode.EliteDrums) ||
                target.Value != profile.CurrentInstrument)
            {
                throw new InvalidDataException(
                    $"Invalid explicit Elite Drums downchart target {target} for {profile.GameMode}/{profile.CurrentInstrument}");
            }

            if (!chart.EliteDrumsDowncharts.TryGetValue(target.Value, out var generated))
            {
                throw new InvalidDataException(
                    $"Elite Drums downchart target {target.Value} was requested but was not generated");
            }

            return generated;
        }
    }
}
