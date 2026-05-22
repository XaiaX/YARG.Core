using System;
using System.IO;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Guitar;
using YARG.Core.Engine.Keys;
using YARG.Core.Engine.Vocals;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Core.IO;

namespace YARG.Core.Replays
{
    public class ReplayFrame
    {
        private static readonly FourCC FRAME_TAG = new('R', 'P', 'F', 'M');

        public readonly YargProfile          Profile;
        public readonly BaseEngineParameters EngineParameters;
        public readonly BaseStats            Stats;
        public readonly GameInput[]          Inputs;

        /// <summary>
        /// Number of microphones in a Party Vocals replay. 0 for non-Party-Vocals (single pitch in Inputs).
        /// </summary>
        public int MicCount;

        /// <summary>
        /// Per-mic pitch streams for Party Vocals replays. MicPitches[i] is the array of pitch values
        /// for microphone i, sampled at the same cadence as GameInput entries in Inputs.
        /// Null for non-Party-Vocals replays.
        /// </summary>
        public float[][] MicPitches;

        public int InputCount => Inputs.Length;

        public ReplayFrame(YargProfile profile, BaseEngineParameters param, BaseStats stats, GameInput[] inputs)
        {
            Profile = profile;
            Stats = stats;
            EngineParameters = param;
            Inputs = inputs;
            MicCount = 0;
            MicPitches = null;
        }

        public ReplayFrame(ref FixedArrayStream stream, int version)
        {
            if (!FRAME_TAG.Matches(ref stream))
            {
                throw new Exception("RPFM tag not found");
            }

            Profile = new YargProfile(ref stream);
            switch (Profile.GameMode)
            {
                case GameMode.FiveFretGuitar:
                case GameMode.SixFretGuitar:
                    EngineParameters = new GuitarEngineParameters(ref stream, version);
                    Stats = new GuitarStats(ref stream, version);
                    break;
                case GameMode.FourLaneDrums:
                case GameMode.FiveLaneDrums:
                case GameMode.EliteDrums:
                    EngineParameters = new DrumsEngineParameters(ref stream, version);
                    Stats = new DrumsStats(ref stream, version);
                    break;
                case GameMode.Vocals:
                    EngineParameters = new VocalsEngineParameters(ref stream, version);
                    Stats = new VocalsStats(ref stream, version);
                    break;
                case GameMode.ProKeys:
                    EngineParameters = new KeysEngineParameters(ref stream, version);
                    Stats = new KeysStats(ref stream, version);
                    break;
                default:
                    throw new InvalidOperationException("Stat creation not implemented.");
            }

            int count = stream.Read<int>(Endianness.Little);
            Inputs = new GameInput[count];
            for (int i = 0; i < count; i++)
            {
                double time = stream.Read<double>(Endianness.Little);
                int action = stream.Read<int>(Endianness.Little);
                int value = stream.Read<int>(Endianness.Little);

                Inputs[i] = new GameInput(time, action, value);
            }

            if (version >= 15)
            {
                MicCount = stream.Read<int>(Endianness.Little);
                if (MicCount > 0)
                {
                    MicPitches = new float[MicCount][];
                    for (int i = 0; i < MicCount; i++)
                    {
                        int len = stream.Read<int>(Endianness.Little);
                        MicPitches[i] = new float[len];
                        for (int j = 0; j < len; j++)
                        {
                            MicPitches[i][j] = stream.Read<float>(Endianness.Little);
                        }
                    }
                }
            }
            else
            {
                MicCount = 0;
                MicPitches = null;
            }
        }

        public void Serialize(BinaryWriter writer)
        {
            FRAME_TAG.Serialize(writer);
            Profile.Serialize(writer);
            EngineParameters.Serialize(writer);
            Stats.Serialize(writer);

            writer.Write(InputCount);
            for (int i = 0; i < InputCount; i++)
            {
                writer.Write(Inputs[i].Time);
                writer.Write(Inputs[i].Action);
                writer.Write(Inputs[i].Integer);
            }

            writer.Write(MicCount);
            if (MicCount > 0 && MicPitches != null)
            {
                for (int i = 0; i < MicCount; i++)
                {
                    writer.Write(MicPitches[i].Length);
                    foreach (float pitch in MicPitches[i])
                    {
                        writer.Write(pitch);
                    }
                }
            }
        }
    }
}