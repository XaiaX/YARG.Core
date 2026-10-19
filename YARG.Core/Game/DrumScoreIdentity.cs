using System;

namespace YARG.Core.Game
{
    /// <summary>
    /// Selected-chart score identity. Extra kick contribution is independent of the authored
    /// extra-content flag recorded in replay reconstruction state.
    /// </summary>
    public readonly struct DrumScoreIdentity : IEquatable<DrumScoreIdentity>
    {
        public Difficulty BaseDifficulty { get; }
        public bool HasExtraKickContribution { get; }
        public DrumScoreCategory ScoreCategory { get; }

        public DrumScoreIdentity(Difficulty baseDifficulty, bool hasExtraKickContribution,
            DrumScoreCategory scoreCategory)
        {
            if (baseDifficulty < Difficulty.Beginner || baseDifficulty > Difficulty.Expert)
                throw new ArgumentOutOfRangeException(nameof(baseDifficulty));
            if (!Enum.IsDefined(typeof(DrumScoreCategory), scoreCategory))
                throw new ArgumentOutOfRangeException(nameof(scoreCategory));
            BaseDifficulty = baseDifficulty;
            HasExtraKickContribution = hasExtraKickContribution;
            ScoreCategory = scoreCategory;
        }

        public bool Equals(DrumScoreIdentity other) => BaseDifficulty == other.BaseDifficulty &&
            HasExtraKickContribution == other.HasExtraKickContribution && ScoreCategory == other.ScoreCategory;
        public override bool Equals(object? obj) => obj is DrumScoreIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(BaseDifficulty, HasExtraKickContribution, ScoreCategory);
        public static bool operator ==(DrumScoreIdentity left, DrumScoreIdentity right) => left.Equals(right);
        public static bool operator !=(DrumScoreIdentity left, DrumScoreIdentity right) => !left.Equals(right);
    }
}
