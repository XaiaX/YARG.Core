using System;
using System.Collections.Generic;

namespace YARG.Core.Game
{
    /// <summary>Authored facts, before kick filtering, conversion or Beginner collapse.</summary>
    public readonly struct DrumSourceTierFacts
    {
        public DrumSourceFormat Source { get; }
        public Difficulty Tier { get; }
        public bool OrdinaryKick { get; }
        public bool ExtraKick { get; }
        public bool OtherPlayable { get; }
        public bool PairedKickFlam { get; }
        public bool ClassicEligible { get; }
        public bool HasPlayable => OrdinaryKick || ExtraKick || OtherPlayable || PairedKickFlam;

        public DrumSourceTierFacts(DrumSourceFormat source, Difficulty tier, bool ordinaryKick,
            bool extraKick, bool otherPlayable, bool pairedKickFlam, bool classicEligible)
        {
            if (!Enum.IsDefined(typeof(DrumSourceFormat), source) || tier < Difficulty.Easy || tier > Difficulty.Expert)
                throw new ArgumentOutOfRangeException(nameof(tier));
            Source = source;
            Tier = tier;
            OrdinaryKick = ordinaryKick || pairedKickFlam;
            ExtraKick = extraKick || pairedKickFlam;
            OtherPlayable = otherPlayable;
            PairedKickFlam = pairedKickFlam;
            ClassicEligible = classicEligible;
        }

        public DrumExtraKickPolicy KickPolicy(bool enableExtra) => enableExtra ? DrumExtraKickPolicy.Include :
            ExtraKick && !OrdinaryKick && !OtherPlayable ? DrumExtraKickPolicy.NormalizeExtraOnly : DrumExtraKickPolicy.Remove;

        public string KickSuffix(bool enableExtra) => !ExtraKick ? string.Empty : enableExtra ? "(2x Kick)" :
            OrdinaryKick || !OtherPlayable ? "(1x Kick)" : "(0x Kick)";
    }

    /// <summary>Pure current-policy selection; historical replay selection remains separate.</summary>
    public static class DrumOutputResolver
    {
        public static ResolvedDrumPlayback? Resolve(IReadOnlyList<DrumSourceTierFacts> sources,
            Instrument output, Difficulty baseTier, bool enableEliteUpconversion,
            bool enableExtraKicks, bool preferEliteDowncharts)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (!ResolvedDrumPlayback.IsOutput(output) || baseTier < Difficulty.Beginner || baseTier > Difficulty.Expert)
                throw new ArgumentOutOfRangeException(nameof(output));
            var tier = baseTier == Difficulty.Beginner ? Difficulty.Easy : baseTier;
            var native = output switch
            {
                Instrument.EliteDrums => DrumSourceFormat.Elite,
                Instrument.FiveLaneDrums => DrumSourceFormat.FiveLane,
                _ => DrumSourceFormat.FourLane,
            };
            var priority = output == Instrument.EliteDrums
                ? new[] { DrumSourceFormat.Elite, DrumSourceFormat.FourLane, DrumSourceFormat.FiveLane }
                : preferEliteDowncharts
                    ? new[] { native, DrumSourceFormat.Elite, native == DrumSourceFormat.FourLane ? DrumSourceFormat.FiveLane : DrumSourceFormat.FourLane }
                    : new[] { native, native == DrumSourceFormat.FourLane ? DrumSourceFormat.FiveLane : DrumSourceFormat.FourLane, DrumSourceFormat.Elite };
            foreach (var source in priority)
            {
                if (output == Instrument.EliteDrums && source != DrumSourceFormat.Elite && !enableEliteUpconversion)
                    continue;
                foreach (var facts in sources)
                {
                    if (facts.Source != source || facts.Tier != tier || !facts.HasPlayable ||
                        (output != Instrument.EliteDrums && !facts.ClassicEligible)) continue;
                    return new ResolvedDrumPlayback(output, source, baseTier, tier,
                        facts.KickPolicy(enableExtraKicks), enableExtraKicks && facts.ExtraKick,
                        ResolvedDrumPlayback.CategoryFor(output, source));
                }
            }
            return null;
        }
    }
}
