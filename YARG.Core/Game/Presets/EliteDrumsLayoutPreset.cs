// pattern: Functional Core
using System;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace YARG.Core.Game
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EliteDrumsLayoutSlotKind { Empty, Ordinary, HiHat, Kick, Kick1, Kick2 }

    /// <summary>A saved editor column. Only Ordinary permits both row assignments.</summary>
    public sealed class EliteDrumsLayoutSlot
    {
        public EliteDrumsLayoutSlotKind Kind;
        public string? Drum;
        public string? Cymbal;

        public EliteDrumsLayoutSlot Copy() => new EliteDrumsLayoutSlot { Kind = Kind, Drum = Drum, Cymbal = Cymbal };
        public static EliteDrumsLayoutSlot Ordinary(string? drum, string? cymbal) =>
            new EliteDrumsLayoutSlot { Kind = EliteDrumsLayoutSlotKind.Ordinary, Drum = drum, Cymbal = cymbal };
        public static EliteDrumsLayoutSlot Exclusive(EliteDrumsLayoutSlotKind kind) =>
            new EliteDrumsLayoutSlot { Kind = kind, Cymbal = kind == EliteDrumsLayoutSlotKind.HiHat ? "hi-hat" : null };
    }

    public sealed class EliteDrumsLayoutMerge
    {
        public string EffectiveId = "";
        public string[] Sources = Array.Empty<string>();
        /// <summary>Null identifies an old unordered merge and uses its historical keeper.</summary>
        public string? PrimarySource;
        public EliteDrumsLayoutMerge Copy() => new EliteDrumsLayoutMerge
        {
            EffectiveId = EffectiveId, Sources = Sources == null ? null! : (string[]) Sources.Clone(),
            PrimarySource = PrimarySource
        };
    }

    public sealed class EliteDrumsLayoutPreset : BasePreset
    {
        public const int CURRENT_SCHEMA_VERSION = 2;
        public const int SLOT_COUNT = 10;
        public static readonly Guid CompactId = new Guid("06871f46-49ea-41ee-a857-a7d0c3022141");
        public static readonly Guid FullId = new Guid("611d5dc0-e3c2-4fd6-8913-693ce1be0a84");

        public int SchemaVersion = CURRENT_SCHEMA_VERSION;
        public EliteDrumsLayoutSlot[] Slots = CreateEmptySlots();
        public EliteDrumsLayoutMerge[] Merges = Array.Empty<EliteDrumsLayoutMerge>();

        public EliteDrumsLayoutPreset(string name = "Elite layout", bool defaultPreset = false) : base(name, defaultPreset)
        {
            Type = nameof(EliteDrumsLayoutPreset);
        }

        public static EliteDrumsLayoutSlot[] CreateEmptySlots() => Enumerable.Range(0, SLOT_COUNT)
            .Select(_ => new EliteDrumsLayoutSlot()).ToArray();

        // Construct fresh objects on each access: callers cannot mutate the built-in definitions.
        public static EliteDrumsLayoutPreset Compact
        {
            get
            {
                var preset = new EliteDrumsLayoutPreset("Compact", true) { Id = CompactId };
                preset.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare", null);
                preset.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat);
                preset.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom1", "left-crash");
                preset.Slots[3] = EliteDrumsLayoutSlot.Ordinary("tom2", "ride");
                preset.Slots[4] = EliteDrumsLayoutSlot.Ordinary("tom3", "right-crash");
                return preset;
            }
        }

        public static EliteDrumsLayoutPreset Full
        {
            get
            {
                var preset = new EliteDrumsLayoutPreset("Full", true) { Id = FullId };
                var sources = new[] { "snare", "hi-hat", "left-crash", "tom1", "tom2", "tom3", "ride", "right-crash" };
                for (int i = 0; i < sources.Length; i++)
                {
                    string source = sources[i];
                    preset.Slots[i] = source == "hi-hat" ? EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat) :
                        EliteDrumsLayoutSources.IsDrum(source) ? EliteDrumsLayoutSlot.Ordinary(source, null) :
                        EliteDrumsLayoutSlot.Ordinary(null, source);
                }
                return preset;
            }
        }

        /// <summary>Deep copy retaining identity, including malformed saved data for recovery.</summary>
        public EliteDrumsLayoutPreset Copy()
        {
            var copy = new EliteDrumsLayoutPreset(Name, DefaultPreset)
            {
                Id = Id, Type = Type, Path = Path, SchemaVersion = SchemaVersion,
                Slots = Slots == null ? null! : Slots.Select(slot => slot?.Copy()!).ToArray(),
                Merges = Merges == null ? null! : Merges.Select(merge => merge?.Copy()!).ToArray()
            };
            copy.UpgradeLegacySchema();
            return copy;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context) => UpgradeLegacySchema();

        private void UpgradeLegacySchema()
        {
            // An explicit primary in v1 could be ignored by old readers; keep it invalid.
            if (SchemaVersion != 1 || Merges == null || Merges.Any(merge => merge?.PrimarySource != null)) return;
            SchemaVersion = CURRENT_SCHEMA_VERSION;
            foreach (var merge in Merges)
                if (merge != null && EliteDrumsCymbalPairs.TryGet(merge.EffectiveId, out var pair))
                    merge.PrimarySource = pair!.PrimarySource;
        }

        public override BasePreset CopyWithNewName(string name)
        {
            var copy = Copy();
            copy.Name = name;
            copy.Id = Guid.NewGuid();
            copy.DefaultPreset = false;
            copy.Path = null;
            return copy;
        }
    }
}
