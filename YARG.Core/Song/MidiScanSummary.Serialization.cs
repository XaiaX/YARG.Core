// pattern: Functional Core
using System;
using System.IO;
using System.Text;
using YARG.Core.Chart;
using YARG.Core.IO;

namespace YARG.Core.Song
{
    public sealed partial class MidiScanSummary
    {
        /// <summary>Versioned compact observations, without retained MIDI events.</summary>
        public byte[] Serialize()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(SEMANTICS_VERSION);
            writer.Write(Resolution);
            writer.Write(_tracks.Length);
            foreach (var track in _tracks)
            {
                byte[] name = Encoding.UTF8.GetBytes(track.MergeName);
                writer.Write(name.Length); writer.Write(name);
                writer.Write(track.ValidName); writer.Write(track.Unsupported); writer.Write((int)track.Type);
                writer.Write((byte)track.Difficulties); writer.Write((byte)track.Elite.eliteDrumsDifficulties);
                writer.Write((byte)track.Elite.downchartDifficulties); writer.Write(track.Playable);
                writer.Write(track.DrumMasks.Length);
                for (int i = 0; i < track.DrumMasks.Length; i++)
                { writer.Write((byte)track.DrumMasks[i]); writer.Write((byte)track.DrumTypes[i]); }
            }
            return stream.ToArray();
        }

        /// <summary>Unknown or invalid observations return null so the caller uses original MIDI.</summary>
        public static MidiScanSummary? Deserialize(byte[] bytes)
        {
            if (bytes == null || bytes.Length > 16 * 1024 * 1024) return null;
            try
            {
                using var stream = new MemoryStream(bytes, false);
                using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
                if (reader.ReadInt32() != SEMANTICS_VERSION) return null;
                long resolution = reader.ReadInt64();
                int count = reader.ReadInt32();
                if (resolution < 0 || resolution > 32767 || count < 0 || count > 65535 || count > bytes.Length / 16) return null;
                var tracks = new TrackObservation[count];
                for (int i = 0; i < count; i++)
                {
                    int length = reader.ReadInt32();
                    if (length < 0 || length > 65536 || length > stream.Length - stream.Position) return null;
                    var track = new TrackObservation { MergeName = new UTF8Encoding(false, true).GetString(reader.ReadBytes(length)),
                        ValidName = ReadBoolean(reader), Unsupported = ReadBoolean(reader), Type = (MidiTrackType)reader.ReadInt32(),
                        Difficulties = ReadMask(reader), Elite = (ReadMask(reader), ReadMask(reader)), Playable = ReadBoolean(reader) };
                    if (!Enum.IsDefined(typeof(MidiTrackType), track.Type)) return null;
                    int masks = reader.ReadInt32();
                    if (masks != 0 && masks != 8) return null;
                    if (track.Type == MidiTrackType.Drums && !track.Unsupported && masks != 8) return null;
                    track.DrumMasks = new DifficultyMask[masks]; track.DrumTypes = new DrumsType[masks];
                    for (int j = 0; j < masks; j++)
                    {
                        track.DrumMasks[j] = ReadMask(reader); byte type = reader.ReadByte();
                        if (type > 7) return null;
                        track.DrumTypes[j] = (DrumsType)type;
                    }
                    tracks[i] = track;
                }
                return stream.Position == stream.Length ? new MidiScanSummary(resolution, tracks) : null;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException) { return null; }
        }
        private static bool ReadBoolean(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new InvalidDataException("Invalid summary boolean.");
            return value != 0;
        }
        private static DifficultyMask ReadMask(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if ((value & ~(byte)DifficultyMask.All) != 0) throw new InvalidDataException("Invalid difficulty mask.");
            return (DifficultyMask)value;
        }
    }
}
