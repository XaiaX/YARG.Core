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
        /// Per-mic input streams for Party Vocals replays. PerMicInputs[i] is the
        /// chronological sequence of GameInput values recorded from mic i during the
        /// live session. Null for non-Party-Vocals replays.
        ///
        /// Each stream is replayed in PartyVocalsPlayer.UpdateInputs by queueing
        /// the inputs into the corresponding sub-engine, matching how live mic input
        /// would flow.
        /// </summary>
        public GameInput[][]? PerMicInputs;

        public int InputCount => Inputs.Length;

        public ReplayFrame(YargProfile profile, BaseEngineParameters param, BaseStats stats, GameInput[] inputs)
        {
            Profile = profile;
            Stats = stats;
            EngineParameters = param;
            Inputs = inputs;
            PerMicInputs = null;
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
                case GameMode.PartyVocals:
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

            if (version >= 16)
            {
                int micCount = stream.Read<int>(Endianness.Little);
                if (micCount > 0)
                {
                    PerMicInputs = new GameInput[micCount][];
                    for (int i = 0; i < micCount; i++)
                    {
                        int len = stream.Read<int>(Endianness.Little);
                        PerMicInputs[i] = new GameInput[len];
                        for (int j = 0; j < len; j++)
                        {
                            double time = stream.Read<double>(Endianness.Little);
                            int action = stream.Read<int>(Endianness.Little);
                            float axis = stream.Read<float>(Endianness.Little);
                            PerMicInputs[i][j] = new GameInput(time, action, axis);
                        }
                    }
                }
                else
                {
                    PerMicInputs = null;
                }
            }
            else if (version == 15)
            {
                // Read and discard legacy MicCount/MicPitches
                int micCount = stream.Read<int>(Endianness.Little);
                for (int i = 0; i < micCount; i++)
                {
                    int len = stream.Read<int>(Endianness.Little);
                    for (int j = 0; j < len; j++)
                    {
                        stream.Read<float>(Endianness.Little); // discard
                    }
                }
            }
            else
            {
                // Version < 15, no mic block existed
                PerMicInputs = null;
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

            writer.Write(PerMicInputs?.Length ?? 0);
            if (PerMicInputs != null)
            {
                foreach (var stream in PerMicInputs)
                {
                    writer.Write(stream.Length);
                    foreach (var input in stream)
                    {
                        // Write fields inline, using Axis (float) for pitch
                        writer.Write(input.Time);
                        writer.Write(input.Action);
                        writer.Write(input.Axis);
                    }
                }
            }
        }
    }
}