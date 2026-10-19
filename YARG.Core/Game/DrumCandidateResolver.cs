using System;

namespace YARG.Core.Game
{
    /// <summary>Native source difficulty presence discovered without loading a playable chart.</summary>
    public readonly struct DrumSourcePresence
    {
        public DifficultyMask FourLane { get; }
        public DifficultyMask FiveLane { get; }
        public DifficultyMask Elite { get; }
        public DifficultyMask EliteDownchart { get; }

        public DrumSourcePresence(DifficultyMask fourLane, DifficultyMask fiveLane,
            DifficultyMask elite, DifficultyMask eliteDownchart)
        {
            FourLane = fourLane;
            FiveLane = fiveLane;
            Elite = elite;
            EliteDownchart = eliteDownchart;
        }

        public bool Has(DrumSourceFormat source, Difficulty difficulty, bool classicOutput = false)
        {
            if (difficulty == Difficulty.Beginner) difficulty = Difficulty.Easy;
            if (difficulty < Difficulty.Easy || difficulty > Difficulty.Expert) return false;
            var mask = source switch
            {
                DrumSourceFormat.FourLane => FourLane,
                DrumSourceFormat.FiveLane => FiveLane,
                DrumSourceFormat.Elite => classicOutput ? Elite & EliteDownchart : Elite,
                _ => DifficultyMask.None,
            };
            return (mask & (DifficultyMask) (1 << (int) difficulty)) != 0;
        }
    }

    /// <summary>A scan-time candidate, not a final playback or kick-policy decision.</summary>
    public readonly struct DrumCandidate
    {
        public Instrument RequestedOutput { get; }
        public DrumSourceFormat SourceFormat { get; }
        public Difficulty BaseDifficulty { get; }
        public Difficulty SourceDifficulty { get; }
        public DrumScoreCategory ScoreCategory { get; }

        internal DrumCandidate(Instrument output, DrumSourceFormat source, Difficulty difficulty)
        {
            RequestedOutput = output;
            SourceFormat = source;
            BaseDifficulty = difficulty;
            SourceDifficulty = difficulty == Difficulty.Beginner ? Difficulty.Easy : difficulty;
            ScoreCategory = ResolvedDrumPlayback.CategoryFor(output, source);
        }
    }

    public static class DrumCandidateResolver
    {
        public static DrumCandidate? Resolve(DrumSourcePresence presence, Instrument requestedOutput,
            Difficulty baseDifficulty, bool enableEliteUpconversion, bool preferEliteDowncharts)
        {
            if (!ResolvedDrumPlayback.IsOutput(requestedOutput) ||
                baseDifficulty < Difficulty.Beginner || baseDifficulty > Difficulty.Expert)
                throw new ArgumentOutOfRangeException(nameof(requestedOutput));

            var native = requestedOutput switch
            {
                Instrument.EliteDrums => DrumSourceFormat.Elite,
                Instrument.FiveLaneDrums => DrumSourceFormat.FiveLane,
                _ => DrumSourceFormat.FourLane,
            };
            var other = native == DrumSourceFormat.FourLane ? DrumSourceFormat.FiveLane : DrumSourceFormat.FourLane;
            var second = requestedOutput == Instrument.EliteDrums ? DrumSourceFormat.FourLane :
                preferEliteDowncharts ? DrumSourceFormat.Elite : other;
            var third = requestedOutput == Instrument.EliteDrums ? DrumSourceFormat.FiveLane :
                preferEliteDowncharts ? other : DrumSourceFormat.Elite;

            for (int i = 0; i < 3; i++)
            {
                var source = i == 0 ? native : i == 1 ? second : third;
                if (requestedOutput == Instrument.EliteDrums && source != DrumSourceFormat.Elite && !enableEliteUpconversion)
                    continue;
                if (presence.Has(source, baseDifficulty, requestedOutput != Instrument.EliteDrums))
                    return new DrumCandidate(requestedOutput, source, baseDifficulty);
            }
            return null;
        }
    }
}
