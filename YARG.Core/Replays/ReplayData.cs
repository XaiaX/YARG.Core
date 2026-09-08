using System.Collections.Generic;
using System;
using System.IO;
using YARG.Core.Extensions;
using YARG.Core.Game;
using Newtonsoft.Json;
using YARG.Core.Utility;
using YARG.Core.IO;

namespace YARG.Core.Replays
{
    public class ReplayData
    {
        private const    int                               PRESETS_VERSION = 0;
        private readonly Dictionary<Guid, ColorProfile>    _colorProfiles;
        private readonly Dictionary<Guid, CameraPreset>    _cameraPresets;
        private readonly Dictionary<Guid, RockMeterPreset> _rockMeterPresets;
        public readonly  bool                              NoFail;
        public readonly  ReplayFrame[]                     Frames;

        public readonly double[] FrameTimes;

        public int PlayerCount => Frames.Length;

        public ReplayData(Dictionary<Guid, ColorProfile> colors, Dictionary<Guid, CameraPreset> cameras,
            Dictionary<Guid, RockMeterPreset> rockMeterPresets, bool noFail, ReplayFrame[] frames, double[] frameTimes)
        {
            _colorProfiles = colors;
            _cameraPresets = cameras;
            _rockMeterPresets = rockMeterPresets;
            NoFail = noFail;
            Frames = frames;
            FrameTimes = frameTimes;
        }

        public ReplayData(FixedArrayStream stream, int version, ReplayReadOptions readOptions)
        {
            int _ = stream.Read<int>(Endianness.Little);
            _colorProfiles = DeserializeDict<ColorProfile>(ref stream);
            _cameraPresets = DeserializeDict<CameraPreset>(ref stream);
            if (version >= 17)
            {
                _rockMeterPresets = DeserializeDict<RockMeterPreset>(ref stream);
                NoFail = stream.ReadBoolean();
            }
            else
            {
                _rockMeterPresets = new Dictionary<Guid, RockMeterPreset>();
                NoFail = false;
            }

            int count = stream.Read<int>(Endianness.Little);
            if (count < 0 || count > 1000000)
            {
                throw new InvalidDataException($"Invalid frame count: {count}");
            }
            Frames = new ReplayFrame[count];
            for (int i = 0; i != count; i++)
            {
                Frames[i] = new ReplayFrame(ref stream, version);
            }

            int frameCount = stream.Read<int>(Endianness.Little);
            if (frameCount < 0 || frameCount > stream.Remaining / sizeof(double))
            {
                // Bound against the remaining bytes rather than a fixed cap: legitimate
                // verbose replays can exceed any constant limit, but the count can never
                // exceed the doubles actually left in the stream. Trailing-byte validation
                // below catches counts that are merely consistent-but-wrong.
                throw new InvalidDataException($"Invalid frame time count: {frameCount} (remaining bytes: {stream.Remaining})");
            }
            var frameTimes = new double[frameCount];

            for (int i = 0; i < frameCount; i++)
            {
                frameTimes[i] = stream.Read<double>(Endianness.Little);
            }

            if (readOptions.KeepFrameTimes)
            {
                FrameTimes = frameTimes;
            }
            else
            {
                FrameTimes = Array.Empty<double>();
            }

            if (stream.Remaining != 0)
            {
                throw new InvalidDataException($"Replay data has {stream.Remaining} trailing bytes");
            }
        }

        public ReadOnlySpan<byte> Serialize()
        {
            // Write all the data for the replay hash
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write(PRESETS_VERSION);
            SerializeDict(writer, _colorProfiles);
            SerializeDict(writer, _cameraPresets);
            SerializeDict(writer, _rockMeterPresets);
            writer.Write(NoFail);

            writer.Write(Frames.Length);
            foreach (var frame in Frames)
            {
                frame.Serialize(writer);
            }

            writer.Write(FrameTimes.Length);
            foreach (var time in FrameTimes)
            {
                writer.Write(time);
            }

            return new ReadOnlySpan<byte>(stream.GetBuffer(), 0, (int) stream.Length);
        }

        /// <summary>
        /// Collects the explicit "Elite (To …)" downchart output formats recorded by the
        /// players in this replay, so a chart can be loaded with the same downchart variants
        /// that were built when the replay was recorded.
        /// </summary>
        /// <returns>
        /// The distinct output instruments, or null when no player used one — which keeps
        /// chart loading byte-for-byte identical to a normal load for every other replay.
        /// </returns>
        /// <remarks>
        /// Frames are filtered through the centralized profile-consistency guard
        /// (<see cref="EliteDrumsDownchartRules.IsDownchartTargetActive"/>): a recorded
        /// target that is malformed, stale (not the frame's current instrument), or not on
        /// a drum game mode is ignored rather than trusted, so a corrupted-but-well-formed
        /// value can never request a mismatched variant.
        /// </remarks>
        public IReadOnlyCollection<Instrument>? GetEliteDrumsDownchartOutputs()
        {
            List<Instrument>? outputs = null;
            foreach (var frame in Frames)
            {
                if (!EliteDrumsDownchartRules.IsDownchartTargetActive(frame.Profile))
                {
                    continue;
                }

                var target = frame.Profile.EliteDrumsDownchartTarget!.Value;
                outputs ??= new List<Instrument>();
                if (!outputs.Contains(target))
                {
                    outputs.Add(target);
                }
            }

            return outputs;
        }

        /// <returns>
        /// The color profile if it's in this container, otherwise, <c>null</c>.
        /// </returns>
        public ColorProfile? GetColorProfile(Guid guid)
        {
            _colorProfiles.TryGetValue(guid, out var color);
            return color;
        }

        /// <returns>
        /// The camera preset if it's in this container, otherwise, <c>null</c>.
        /// </returns>
        public CameraPreset? GetCameraPreset(Guid guid)
        {
            _cameraPresets.TryGetValue(guid, out var preset);
            return preset;
        }

        public RockMeterPreset? GetRockMeterPreset(Guid guid)
        {
            _rockMeterPresets.TryGetValue(guid, out var preset);
            return preset;
        }

        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Converters =
            {
                new JsonColorConverter()
            }
        };

        private static void SerializeDict<T>(BinaryWriter writer, Dictionary<Guid, T> dict)
        {
            writer.Write(dict.Count);
            foreach (var (key, value) in dict)
            {
                // Write key
                writer.Write(key);

                // Write preset
                var json = JsonConvert.SerializeObject(value, _jsonSettings);
                writer.Write(json);
            }
        }

        private static Dictionary<Guid, T> DeserializeDict<T>(ref FixedArrayStream stream)
        {
            var dict = new Dictionary<Guid, T>();
            int len = stream.Read<int>(Endianness.Little);
            if (len < 0 || len > 10000)
            {
                throw new InvalidDataException($"Invalid preset count: {len}");
            }
            for (int i = 0; i < len; i++)
            {
                // Read key
                var guid = stream.ReadGuid();

                // Read preset
                var json = stream.ReadString();
                var preset = JsonConvert.DeserializeObject<T>(json, _jsonSettings)!;

                dict.Add(guid, preset);
            }
            return dict;
        }
    }
}
