// pattern: Imperative Shell
using System;
using System.IO;
using YARG.Core.Extensions;
using YARG.Core.IO;
using YARG.Core.Replays;

namespace YARG.Core.Engine.Drums
{
    public class DrumsEngineParameters : BaseEngineParameters
    {
        public const byte LEGACY_ELITE_FILL_RULESET = 0;
        public const byte ELITE_FILL_RULESET_V1 = 1;
        public const float DEFAULT_ENTRY_GRACE_MULTIPLIER = 2f;
        public const double DEFAULT_HI_HAT_CLOSURE_WINDOW = .030;

        public readonly bool HiHatClosureSuppression;
        /// <summary>Physical seconds, before song-speed conversion.</summary>
        public readonly double HiHatClosureWindow;

        public enum DrumMode : byte
        {
            NonProFourLane,
            ProFourLane,
            FiveLane,
            Elite
        }

        /// <summary>
        /// What mode the inputs should be processed in.
        /// </summary>
        public readonly DrumMode Mode;

        //Ghost notes are below this threshold, Accent notes are above 1 - threshold
        public readonly float VelocityThreshold;

        // The maximum allowed time (seconds) between notes to use context-sensitive velocity scoring
        public readonly float SituationalVelocityWindow;

        // Whether or not we can earn more star power while its already active
        public readonly bool NoStarPowerOverlap;

        /// <summary>Version of the Elite hand-fill rules used by this parameter payload.</summary>
        public readonly byte EliteFillRuleset;

        /// <summary>Multiplier applied to the V1 fill entry grace window.</summary>
        public readonly float EntryGraceMultiplier;

        public DrumsEngineParameters(HitWindowSettings hitWindow, int maxMultiplier, float[] starMultiplierThresholds, float[] soloBonusStarMultiplierThresholds,
            DrumMode mode, bool noStarPowerOverlap, bool enableLanes, byte eliteFillRuleset = 0,
            float entryGraceMultiplier = DEFAULT_ENTRY_GRACE_MULTIPLIER,
            bool hiHatClosureSuppression = false, double hiHatClosureWindow = DEFAULT_HI_HAT_CLOSURE_WINDOW)
            : base(hitWindow, maxMultiplier, 0, 0, starMultiplierThresholds, soloBonusStarMultiplierThresholds, enableLanes)
        {
            Mode = mode;
            VelocityThreshold = 0.35f;
            SituationalVelocityWindow = 1.5f;
            NoStarPowerOverlap = noStarPowerOverlap;
            EliteFillRuleset = eliteFillRuleset;
            EntryGraceMultiplier = entryGraceMultiplier;
            HiHatClosureSuppression = hiHatClosureSuppression;
            HiHatClosureWindow = hiHatClosureWindow;
            ValidateEliteFillParameters();
            ValidateHiHatClosureWindow();
        }

        public DrumsEngineParameters(ref FixedArrayStream stream, int version)
            : base(ref stream, version)
        {
            Mode = (DrumMode) stream.ReadByte();
            VelocityThreshold = stream.Read<float>(Endianness.Little);
            SituationalVelocityWindow = stream.Read<float>(Endianness.Little);
            if (version >= 9) {
                NoStarPowerOverlap = stream.ReadBoolean();
            }

            // Replay v20 adds Elite fill parameters after the existing drum payload.
            // Older payloads are intentionally interpreted as legacy behavior.
            if (version >= ReplayIO.ELITE_FILL_MIN)
            {
                EliteFillRuleset = stream.ReadByte();
                EntryGraceMultiplier = stream.Read<float>(Endianness.Little);
                ValidateEliteFillParameters();
            }
            else
            {
                EliteFillRuleset = LEGACY_ELITE_FILL_RULESET;
                EntryGraceMultiplier = DEFAULT_ENTRY_GRACE_MULTIPLIER;
            }
            if (version >= ReplayIO.HI_HAT_CLOSURE_MIN)
            {
                HiHatClosureSuppression = stream.ReadBoolean();
                HiHatClosureWindow = stream.Read<double>(Endianness.Little);
                ValidateHiHatClosureWindow();
            }
            else
            {
                HiHatClosureSuppression = false;
                HiHatClosureWindow = DEFAULT_HI_HAT_CLOSURE_WINDOW;
            }
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.Write((byte) Mode);
            writer.Write(VelocityThreshold);
            writer.Write(SituationalVelocityWindow);
            writer.Write(NoStarPowerOverlap);
            writer.Write(EliteFillRuleset);
            writer.Write(EntryGraceMultiplier);
            writer.Write(HiHatClosureSuppression);
            writer.Write(HiHatClosureWindow);
        }

        private void ValidateHiHatClosureWindow()
        {
            if (double.IsNaN(HiHatClosureWindow) || double.IsInfinity(HiHatClosureWindow) ||
                HiHatClosureWindow <= 0 || HiHatClosureWindow > .1)
                throw new InvalidDataException($"Invalid hi-hat closure window: {HiHatClosureWindow}");
        }

        private void ValidateEliteFillParameters()
        {
            if (EliteFillRuleset > ELITE_FILL_RULESET_V1)
            {
                throw new InvalidDataException($"Unsupported Elite fill ruleset: {EliteFillRuleset}");
            }

            if (float.IsNaN(EntryGraceMultiplier) || float.IsInfinity(EntryGraceMultiplier) ||
                EntryGraceMultiplier <= 0)
            {
                throw new InvalidDataException($"Invalid Elite entry grace multiplier: {EntryGraceMultiplier}");
            }
        }

        public override string ToString()
        {
            return
                $"{base.ToString()}\n" +
                $"Velocity threshold: {VelocityThreshold}\n" +
                $"Situational velocity window: {SituationalVelocityWindow}\n" +
                $"No star power overlap: {NoStarPowerOverlap}";
        }
    }
}
