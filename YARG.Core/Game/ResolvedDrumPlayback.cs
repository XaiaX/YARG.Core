using System;
using System.IO;

namespace YARG.Core.Game
{
    // Serialized values are stable; append new values rather than reordering them.
    public enum DrumSourceFormat : byte { FourLane = 0, FiveLane = 1, Elite = 2 }
    public enum DrumExtraKickPolicy : byte { Include = 0, Remove = 1, NormalizeExtraOnly = 2 }
    public enum DrumScoreCategory : byte
    {
        Classic = 0,
        NativeElite = 1,
        FourLaneDerivedElite = 2,
        FiveLaneDerivedElite = 3,
        LegacyUnknownElite = 4,
    }

    /// <summary>Immutable chart reconstruction state captured once for a player's song.</summary>
    public sealed class ResolvedDrumPlayback
    {
        public const byte CURRENT_POLICY_VERSION = 1;
        public byte PolicyVersion { get; }
        public Instrument RequestedOutput { get; }
        public DrumSourceFormat SourceFormat { get; }
        public Difficulty BaseDifficulty { get; }
        public Difficulty SourceDifficulty { get; }
        public DrumExtraKickPolicy ExtraKickPolicy { get; }
        public bool EffectiveExtraContent { get; }
        public DrumScoreCategory ScoreCategory { get; }

        public ResolvedDrumPlayback(Instrument requestedOutput, DrumSourceFormat sourceFormat,
            Difficulty baseDifficulty, Difficulty sourceDifficulty, DrumExtraKickPolicy extraKickPolicy,
            bool effectiveExtraContent, DrumScoreCategory scoreCategory,
            byte policyVersion = CURRENT_POLICY_VERSION)
        {
            if (policyVersion != CURRENT_POLICY_VERSION || !IsOutput(requestedOutput) ||
                !Enum.IsDefined(typeof(DrumSourceFormat), sourceFormat) ||
                baseDifficulty < Difficulty.Beginner || baseDifficulty > Difficulty.Expert ||
                sourceDifficulty != (baseDifficulty == Difficulty.Beginner ? Difficulty.Easy : baseDifficulty) ||
                !Enum.IsDefined(typeof(DrumExtraKickPolicy), extraKickPolicy) ||
                scoreCategory != CategoryFor(requestedOutput, sourceFormat) ||
                (effectiveExtraContent && extraKickPolicy != DrumExtraKickPolicy.Include))
            {
                throw new InvalidDataException("Invalid or unsupported resolved drum playback state");
            }
            PolicyVersion = policyVersion;
            RequestedOutput = requestedOutput;
            SourceFormat = sourceFormat;
            BaseDifficulty = baseDifficulty;
            SourceDifficulty = sourceDifficulty;
            ExtraKickPolicy = extraKickPolicy;
            EffectiveExtraContent = effectiveExtraContent;
            ScoreCategory = scoreCategory;
        }

        public static bool IsOutput(Instrument instrument) => instrument is Instrument.FourLaneDrums or
            Instrument.ProDrums or Instrument.FiveLaneDrums or Instrument.EliteDrums;

        public static DrumScoreCategory CategoryFor(Instrument output, DrumSourceFormat source) =>
            output != Instrument.EliteDrums ? DrumScoreCategory.Classic : source switch
            {
                DrumSourceFormat.Elite => DrumScoreCategory.NativeElite,
                DrumSourceFormat.FourLane => DrumScoreCategory.FourLaneDerivedElite,
                DrumSourceFormat.FiveLane => DrumScoreCategory.FiveLaneDerivedElite,
                _ => throw new InvalidDataException("Unknown authored drum source"),
            };

        public ResolvedDrumPlayback Copy() => new(RequestedOutput, SourceFormat, BaseDifficulty,
            SourceDifficulty, ExtraKickPolicy, EffectiveExtraContent, ScoreCategory, PolicyVersion);

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(PolicyVersion);
            writer.Write((byte) RequestedOutput);
            writer.Write((byte) SourceFormat);
            writer.Write((byte) BaseDifficulty);
            writer.Write((byte) SourceDifficulty);
            writer.Write((byte) ExtraKickPolicy);
            writer.Write(EffectiveExtraContent);
            writer.Write((byte) ScoreCategory);
        }

        public static ResolvedDrumPlayback Deserialize(ref YARG.Core.IO.FixedArrayStream stream)
        {
            byte version = stream.ReadByte();
            var output = (Instrument) stream.ReadByte();
            var source = (DrumSourceFormat) stream.ReadByte();
            var tier = (Difficulty) stream.ReadByte();
            var sourceTier = (Difficulty) stream.ReadByte();
            var kicks = (DrumExtraKickPolicy) stream.ReadByte();
            byte extra = stream.ReadByte();
            var category = (DrumScoreCategory) stream.ReadByte();
            if (extra > 1) throw new InvalidDataException("Invalid extra-content flag");
            return new ResolvedDrumPlayback(output, source, tier, sourceTier, kicks, extra != 0, category, version);
        }

        public static ResolvedDrumPlayback Deserialize(BinaryReader reader)
        {
            byte version = reader.ReadByte();
            var output = (Instrument) reader.ReadByte();
            var source = (DrumSourceFormat) reader.ReadByte();
            var tier = (Difficulty) reader.ReadByte();
            var sourceTier = (Difficulty) reader.ReadByte();
            var kicks = (DrumExtraKickPolicy) reader.ReadByte();
            byte extra = reader.ReadByte();
            var category = (DrumScoreCategory) reader.ReadByte();
            if (extra > 1) throw new InvalidDataException("Invalid extra-content flag");
            return new ResolvedDrumPlayback(output, source, tier, sourceTier, kicks, extra != 0, category, version);
        }
    }
}
