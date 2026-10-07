using System.Drawing;
using System.IO;
using Newtonsoft.Json;
using YARG.Core.Game.Settings;
using YARG.Core.Utility;

namespace YARG.Core.Game
{
    public partial class ColorProfile : BasePreset, IBinarySerializable
    {
        private const int COLOR_PROFILE_VERSION = 4;

        /// <summary>
        /// Interface that has methods that allows for generic fret color retrieval.
        /// Not all instruments have frets, so it's an interface.
        /// </summary>
        public interface IFretColorProvider
        {
            public Color GetFretColor(int index);
            public Color GetFretInnerColor(int index);
            public Color GetParticleColor(int index);
        }

        [JsonIgnore]
        public int Version = COLOR_PROFILE_VERSION;

        [SettingSubSection]
        public FiveFretGuitarColors FiveFretGuitar;
        [SettingSubSection]
        public SixFretGuitarColors SixFretGuitar;
        [SettingSubSection]
        public FourLaneDrumsColors FourLaneDrums;
        [SettingSubSection]
        public FiveLaneDrumsColors FiveLaneDrums;
        [SettingSubSection]
        public ProKeysColors ProKeys;
        [SettingSubSection]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public EliteDrumsColors EliteDrums;

        public ColorProfile(string name, bool defaultPreset = false) : base(name, defaultPreset)
        {
            FiveFretGuitar = new FiveFretGuitarColors();
            SixFretGuitar = new SixFretGuitarColors();
            FourLaneDrums = new FourLaneDrumsColors();
            FiveLaneDrums = new FiveLaneDrumsColors();
            ProKeys = new ProKeysColors();
            EliteDrums = new EliteDrumsColors();
        }

        public override BasePreset CopyWithNewName(string name)
        {
            return new ColorProfile(name)
            {
                FiveFretGuitar = FiveFretGuitar.Copy(),
                SixFretGuitar = SixFretGuitar.Copy(),
                FourLaneDrums = FourLaneDrums.Copy(),
                FiveLaneDrums = FiveLaneDrums.Copy(),
                ProKeys = ProKeys.Copy(),
                EliteDrums = (EliteDrums ?? new EliteDrumsColors()).Copy(),
            };
        }

        public void Serialize(BinaryWriter writer)
        {
            // Always emit the current layout, even after loading an older profile.
            writer.Write(COLOR_PROFILE_VERSION);
            writer.Write(Name);

            FiveFretGuitar.Serialize(writer);
            SixFretGuitar.Serialize(writer);
            FourLaneDrums.Serialize(writer);
            FiveLaneDrums.Serialize(writer);
            ProKeys.Serialize(writer);
            (EliteDrums ?? new EliteDrumsColors()).Serialize(writer);
        }

        public void Deserialize(BinaryReader reader, int version = 0)
        {
            version = reader.ReadInt32();
            Name = reader.ReadString();

            FiveFretGuitar.Deserialize(reader, version);
            SixFretGuitar.Deserialize(reader, version);
            FourLaneDrums.Deserialize(reader, version);
            FiveLaneDrums.Deserialize(reader, version);
            ProKeys.Deserialize(reader, version);
            EliteDrums = new EliteDrumsColors();
            if (version >= 3)
            {
                EliteDrums.Deserialize(reader, version);
            }
            Version = COLOR_PROFILE_VERSION;
        }
    }
}
