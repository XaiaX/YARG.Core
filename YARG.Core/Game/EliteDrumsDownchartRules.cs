using YARG.Core.Song;

namespace YARG.Core.Game
{
    /// <summary>
    /// Central rules for the experimental explicit "Elite (To …)" downchart targets
    /// (see <see cref="YargProfile.EliteDrumsDownchartTarget"/>). One shared
    /// definition used by Difficulty Select, Maestro setup, gameplay chart loading,
    /// and the replay analyzer, so row offering, validation, difficulty calculation,
    /// and track selection can never disagree about which targets are valid or when
    /// a song is playable for one.
    /// </summary>
    public static class EliteDrumsDownchartRules
    {
        /// <summary>
        /// The only instruments that are valid downchart output targets: the three
        /// lane-based drums formats the downchart builder can emit. Anything else —
        /// including malformed or stale serialized values — is genuinely invalid.
        /// </summary>
        public static bool IsValidTarget(Instrument target)
        {
            return target is Instrument.FourLaneDrums or Instrument.ProDrums
                or Instrument.FiveLaneDrums;
        }

        /// <summary>
        /// Nullable form of <see cref="IsValidTarget(Instrument)"/>: a null target is
        /// simply absent (not invalid-but-present), so this only accepts non-null
        /// values inside the valid target domain.
        /// </summary>
        public static bool IsValidTarget(Instrument? target)
        {
            return target is { } instrument && IsValidTarget(instrument);
        }

        /// <summary>
        /// Whether a song is playable for a downchart target: the song's Elite Drums
        /// chart produces a usable (non-empty) downchart for the show — the exact notion
        /// the chart loader exposes downchart variants by, recorded at scan time in
        /// <see cref="SongEntry.HasEliteDrumsDownchart"/> — or, for songs without one,
        /// the target format's native chart (with the usual 4/5-lane conversions)
        /// carries it. This is the session playability predicate Difficulty Select and
        /// Maestro must share: a target row is only offered — and a staged target only
        /// kept — when every song in the show satisfies it. A target outside the valid
        /// domain is never playable for any song, so malformed values fail closed.
        /// </summary>
        /// <remarks>
        /// "Active Elite chart" alone is deliberately NOT enough: an Elite chart
        /// consisting solely of notes the downchart builder drops (unforced or invisible
        /// hat pedal notes) generates empty difficulties, and
        /// <c>MoonSongLoader.DownchartEliteDrumsTrack</c> then builds no downchart at
        /// all — a target offered on chart presence alone could resolve to no usable
        /// track when no native chart exists either.
        /// </remarks>
        public static bool IsSongPlayableForTarget(SongEntry song, Instrument target)
        {
            if (!IsValidTarget(target))
            {
                return false;
            }

            return song.HasEliteDrumsDownchart() ||
                HasNativePlayableInstrument(song, target);
        }

        /// <summary>
        /// Whether a difficulty is playable for a target in a song. Songs with a usable
        /// downchart take their difficulties from it (Beginner is synthesized from Easy,
        /// matching the Core downchart loader); songs whose Elite chart downcharts to
        /// nothing keep their native difficulties because no downchart is built for them.
        /// A target outside the valid domain offers no difficulties, so malformed values
        /// fail closed.
        /// </summary>
        public static bool HasTargetDifficulty(SongEntry song, Instrument target,
            Difficulty difficulty)
        {
            if (!IsValidTarget(target))
            {
                return false;
            }

            if (song.HasEliteDrumsDownchart())
            {
                return song.HasEliteDrumsDownchartDifficulty(difficulty);
            }

            return HasNativeDifficulty(song, target, difficulty);
        }

        /// <summary>
        /// Whether a profile's "Elite (To …)" downchart target is actually active and
        /// consistent with the rest of the profile: a well-formed target (only
        /// 4-lane/Pro/5-lane), still equal to <see cref="YargProfile.CurrentInstrument"/>
        /// (so the engine mode, highway, and track lookup all agree on the output
        /// format), and a drum game mode that supports downchart outputs. This is the
        /// single consistency guard gameplay (<c>DrumsPlayer</c>), replay loading
        /// (<c>ReplayAnalyzer</c>, <c>ReplayData</c>), and chart-output collection
        /// (<c>GameManager</c>) consult, so a corrupted or stale — but well-formed —
        /// target falls back to the native track everywhere instead of selecting a
        /// mismatched downchart variant.
        /// </summary>
        /// <remarks>
        /// The live experimental toggle is deliberately NOT part of this predicate: live
        /// chart loading applies it when collecting downchart outputs (and a missing
        /// downchart falls back native anyway), while replays must reproduce their
        /// recorded targets regardless of this machine's toggle.
        /// </remarks>
        public static bool IsDownchartTargetActive(YargProfile profile)
        {
            return profile.EliteDrumsDownchartTarget is { } target &&
                IsValidTarget(target) &&
                target == profile.CurrentInstrument &&
                profile.GameMode is GameMode.FourLaneDrums or GameMode.FiveLaneDrums
                    or GameMode.EliteDrums;
        }

        /// <summary>
        /// Native chart presence for a drums output format, mirroring the menu's
        /// playable-instrument rules for drums (4-lane/Pro charts are playable on
        /// 5-lane and vice versa). Rejects instruments outside the target domain so a
        /// malformed value can never reach the song part lookups.
        /// </summary>
        private static bool HasNativePlayableInstrument(SongEntry song, Instrument target)
        {
            if (!IsValidTarget(target))
            {
                return false;
            }

            return song.HasInstrument(target) || target switch
            {
                // Allow 5 -> 4-lane conversions to be played on 4-lane
                Instrument.FourLaneDrums or
                Instrument.ProDrums      => song.HasInstrument(Instrument.FiveLaneDrums),
                // Allow 4 -> 5-lane conversions to be played on 5-lane
                Instrument.FiveLaneDrums => song.HasInstrument(Instrument.ProDrums),
                _ => false
            };
        }

        private static bool HasNativeDifficulty(SongEntry song, Instrument target,
            Difficulty difficulty)
        {
            if (!IsValidTarget(target))
            {
                return false;
            }

            return song[target][difficulty] || target switch
            {
                // Allow 5 -> 4-lane conversions to be played on 4-lane
                Instrument.FourLaneDrums or
                Instrument.ProDrums      => song[Instrument.FiveLaneDrums][difficulty],
                // Allow 4 -> 5-lane conversions to be played on 5-lane
                Instrument.FiveLaneDrums => song[Instrument.ProDrums][difficulty],
                _ => false
            };
        }
    }
}
