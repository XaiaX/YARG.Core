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
        /// Resolves a native Elite request at the requested difficulty. Native Elite wins when
        /// it contains playable notes; otherwise Pro, Four-Lane, then Five-Lane are checked in
        /// that fixed order. Empty/missing difficulties and event-only charts are not playable.
        /// An explicit generated target is strict and never participates in native fallback.
        /// Returns null if no native track has playable notes at this difficulty.
        /// </summary>
        public static Instrument? ResolveNativeEliteRequest(SongChart chart, YargProfile profile,
            Difficulty difficulty)
        {
            if (chart is null) throw new ArgumentNullException(nameof(chart));
            if (profile is null) throw new ArgumentNullException(nameof(profile));
            if (profile.GameMode is not GameMode.EliteDrums)
            {
                throw new InvalidDataException($"Profile {profile.GameMode} is not an Elite Drums request");
            }

            if (profile.EliteDrumsDownchartTarget is not null)
            {
                var generated = SelectTrack(chart, profile);
                if (!generated.TryGetDifficulty(difficulty, out var targetDifficulty) ||
                    targetDifficulty.Notes.Count == 0)
                {
                    throw new InvalidDataException(
                        $"Elite Drums downchart target {profile.EliteDrumsDownchartTarget} has no playable {difficulty} notes");
                }

                return profile.EliteDrumsDownchartTarget;
            }

            if (chart.EliteDrums.TryGetDifficulty(difficulty, out var eliteDifficulty) &&
                eliteDifficulty.Notes.Exists(note => !note.IsInvisibleTerminator))
            {
                return Instrument.EliteDrums;
            }

            foreach (var instrument in new[] { Instrument.ProDrums, Instrument.FourLaneDrums,
                         Instrument.FiveLaneDrums })
            {
                if (chart.GetDrumsTrack(instrument).TryGetDifficulty(difficulty, out var nativeDifficulty) &&
                    nativeDifficulty.Notes.Count > 0)
                {
                    return instrument;
                }
            }

            return null;
        }

        /// <summary>
        /// True only for a native Elite Drums profile. An explicit generated target always
        /// takes precedence (and is validated by SelectTrack), even when the instrument is Elite.
        /// </summary>
        public static bool UsesNativeEliteTrack(YargProfile profile)
        {
            if (profile is null) throw new ArgumentNullException(nameof(profile));
            return profile.EliteDrumsDownchartTarget is null &&
                profile.GameMode is GameMode.EliteDrums &&
                profile.CurrentInstrument is Instrument.EliteDrums;
        }

        /// <summary>
        /// Selects the native Elite track for a profile that actually plays native Elite notes.
        /// An explicit generated target must never fall back to this track.
        /// </summary>
        public static InstrumentTrack<EliteDrumNote> SelectNativeEliteTrack(SongChart chart, YargProfile profile)
        {
            if (chart is null) throw new ArgumentNullException(nameof(chart));
            if (profile is null) throw new ArgumentNullException(nameof(profile));
            if (!UsesNativeEliteTrack(profile))
            {
                throw new InvalidDataException(
                    $"Profile {profile.GameMode}/{profile.CurrentInstrument} does not select native Elite Drums");
            }

            return chart.EliteDrums;
        }

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
