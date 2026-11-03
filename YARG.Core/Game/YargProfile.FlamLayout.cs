// pattern: Functional Core
using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace YARG.Core.Game
{
    public partial class YargProfile
    {
        // New profiles choose V explicitly. Legacy JSON resets this before
        // population so startup can save it without consuming its pending migration.
        private DrumFlamLayout? _flamLayout = DrumFlamLayout.V;

        [JsonIgnore]
        public DrumFlamLayout FlamLayout
        {
            get => _flamLayout ?? DrumFlamLayout.V;
            set
            {
                if (!IsValidFlamLayout(value))
                    throw new ArgumentOutOfRangeException(nameof(value), value, "unsupported drum flam layout");
                _flamLayout = value;
            }
        }

        [JsonProperty("FlamLayout", NullValueHandling = NullValueHandling.Ignore)]
        private DrumFlamLayout? SerializedFlamLayout
        {
            get => _flamLayout;
            set
            {
                if (value.HasValue) FlamLayout = value.Value;
                else _flamLayout = null;
            }
        }

        [JsonIgnore]
        public bool NeedsFlamLayoutMigration => !_flamLayout.HasValue;

        /// <summary>Seeds only legacy profiles after the old global setting is available.</summary>
        public bool MigrateFlamLayout(DrumFlamLayout legacy)
        {
            if (!NeedsFlamLayoutMigration) return false;
            FlamLayout = IsValidFlamLayout(legacy) ? legacy : DrumFlamLayout.V;
            return true;
        }

        [OnDeserializing]
        private void PrepareFlamLayoutJsonDeserialization(StreamingContext context)
        {
            _flamLayout = null;
        }

        private static bool IsValidFlamLayout(DrumFlamLayout value) =>
            value is DrumFlamLayout.V or DrumFlamLayout.Bucket or DrumFlamLayout.Parallel;
    }
}
