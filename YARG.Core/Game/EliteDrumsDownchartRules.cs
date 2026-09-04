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
        /// <see cref="SongEntry.HasEliteDrumsDownchart"/>. An explicit Elite target
        /// never falls back to a native chart: without an Elite source, the option would
        /// merely act as an alias for ordinary native selection. This is the session
        /// playability predicate Difficulty Select and Maestro must share: a target row
        /// is only offered — and a staged target only kept — when every song in the show
        /// satisfies it. A target outside the valid domain is never playable for any
        /// song, so malformed values fail closed.
        /// </summary>
        /// <remarks>
        /// "Active Elite chart" alone is deliberately NOT enough: an Elite chart
        /// consisting solely of notes the downchart builder drops (unforced or invisible
        /// hat pedal notes) generates empty difficulties, and
        /// <c>MoonSongLoader.DownchartEliteDrumsTrack</c> then builds no downchart at
        /// all. Native chart availability is intentionally irrelevant to this explicit
        /// target predicate; ordinary native instrument selection retains its own
        /// fallback behavior elsewhere.
        /// </remarks>
        public static bool IsSongPlayableForTarget(SongEntry song, Instrument target)
        {
            return IsValidTarget(target) && song.HasEliteDrumsDownchart();
        }

        /// <summary>
        /// Whether a difficulty is playable for an explicit Elite target in a song.
        /// Usable Elite downcharts provide their generated difficulties (Beginner is
        /// synthesized from Easy, matching the Core downchart loader). A song without a
        /// usable Elite downchart provides no explicit-target difficulties, even when a
        /// native drums chart has the requested difficulty; native fallback belongs to
        /// ordinary instrument selection, not an explicit Elite target.
        /// A target outside the valid domain offers no difficulties, so malformed values
        /// fail closed.
        /// </summary>
        public static bool HasTargetDifficulty(SongEntry song, Instrument target,
            Difficulty difficulty)
        {
            return IsValidTarget(target) && song.HasEliteDrumsDownchart() &&
                song.HasEliteDrumsDownchartDifficulty(difficulty);
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

    }
}
