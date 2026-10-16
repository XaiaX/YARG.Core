using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YARG.Core.Extensions;

namespace YARG.Core.Game
{
    public partial class ColorProfile
    {
        [JsonConverter(typeof(EliteDrumsColorsJsonConverter))]
        public partial class EliteDrumsColors
        {
            /// <summary>Stored saved slots use ZERO-based indexes 0..9 (Fret1..Fret10).</summary>
            public const int SAVED_SLOT_COUNT = 10;

            // Numbers identify saved columns, not compacted playback lanes or kit pieces.
            // Slots without a legacy source (5..9) use deterministic repeating defaults.
            public Color Fret1 = DefaultRed;
            public Color Fret2 = DefaultYellow;
            public Color Fret3 = DefaultBlue;
            public Color Fret4 = DefaultOrange;
            public Color Fret5 = DefaultGreen;
            public Color Fret6 = DefaultRed;
            public Color Fret7 = DefaultYellow;
            public Color Fret8 = DefaultBlue;
            public Color Fret9 = DefaultOrange;
            public Color Fret10 = DefaultGreen;
            public Color Fret1Inner = DefaultRed;
            public Color Fret2Inner = DefaultYellow;
            public Color Fret3Inner = DefaultBlue;
            public Color Fret4Inner = DefaultOrange;
            public Color Fret5Inner = DefaultGreen;
            public Color Fret6Inner = DefaultRed;
            public Color Fret7Inner = DefaultYellow;
            public Color Fret8Inner = DefaultBlue;
            public Color Fret9Inner = DefaultOrange;
            public Color Fret10Inner = DefaultGreen;

            /// <summary>Outer fret color for saved slot <paramref name="savedSlot"/> (0..9 -> Fret1..Fret10).</summary>
            public Color GetSlotFretColor(int savedSlot) => savedSlot switch
            {
                0 => Fret1, 1 => Fret2, 2 => Fret3, 3 => Fret4, 4 => Fret5,
                5 => Fret6, 6 => Fret7, 7 => Fret8, 8 => Fret9, 9 => Fret10,
                _ => throw new ArgumentOutOfRangeException(nameof(savedSlot))
            };
            /// <summary>Inner fret color for saved slot <paramref name="savedSlot"/> (0..9 -> Fret1..Fret10).</summary>
            public Color GetSlotFretInnerColor(int savedSlot) => savedSlot switch
            {
                0 => Fret1Inner, 1 => Fret2Inner, 2 => Fret3Inner, 3 => Fret4Inner, 4 => Fret5Inner,
                5 => Fret6Inner, 6 => Fret7Inner, 7 => Fret8Inner, 8 => Fret9Inner, 9 => Fret10Inner,
                _ => throw new ArgumentOutOfRangeException(nameof(savedSlot))
            };

            private void SerializeSlotColors(BinaryWriter writer)
            {
                for (int slot = 0; slot < SAVED_SLOT_COUNT; slot++)
                {
                    writer.Write(GetSlotFretColor(slot));
                    writer.Write(GetSlotFretInnerColor(slot));
                }
            }
            private void DeserializeSlotColors(BinaryReader reader)
            {
                for (int slot = 0; slot < SAVED_SLOT_COUNT; slot++)
                {
                    typeof(EliteDrumsColors).GetField($"Fret{slot + 1}").SetValue(this, reader.ReadColor());
                    typeof(EliteDrumsColors).GetField($"Fret{slot + 1}Inner").SetValue(this, reader.ReadColor());
                }
            }
            private void MigrateSlotColors()
            {
                // Only the five legacy hand positions migrate, into saved slots 0..4.
                // DoubleKick keeps its own legacy styling for its real identity and never
                // fills a saved slot; slots 5..9 keep their deterministic defaults.
                Fret1 = SnareFret; Fret1Inner = SnareFretInner;
                Fret2 = HatFret; Fret2Inner = HatFretInner;
                Fret3 = LeftCrashTom1Fret; Fret3Inner = LeftCrashTom1FretInner;
                Fret4 = RideTom2Fret; Fret4Inner = RideTom2FretInner;
                Fret5 = RightCrashTom3Fret; Fret5Inner = RightCrashTom3FretInner;
            }

            private static string? GetLegacyJsonSource(string name)
            {
                var positions = new[] { "Snare", "Hat", "LeftCrashTom1", "RideTom2", "RightCrashTom3" };
                if (name.StartsWith("Fret", StringComparison.Ordinal))
                {
                    bool inner = name.EndsWith("Inner", StringComparison.Ordinal);
                    string number = name.Substring(4, name.Length - 4 - (inner ? 5 : 0));
                    if (int.TryParse(number, out int slot) && slot >= 1 && slot <= positions.Length)
                        return positions[slot - 1] + (inner ? "FretInner" : "Fret");
                    return null;
                }
                int input = name.IndexOf("Input", StringComparison.Ordinal);
                if (input < 0) return null;
                string role = name.Substring(0, input);
                string position = role switch
                {
                    "HatIndifferent" or "HatOpen" or "HatClosed" or "Stomp" or "Splash" => "Hat",
                    "LeftCrash" or "Tom1" => "LeftCrashTom1",
                    "Ride" or "Tom2" => "RideTom2",
                    "RightCrash" or "Tom3" => "RightCrashTom3",
                    "KickFlam" => "Kick", "HandFlam" => "Snare", _ => role
                };
                return position + (name.EndsWith("InputEffect", StringComparison.Ordinal) ? "Particles" : "Fret");
            }

            internal void MigrateMissingJsonColors(JObject json)
            {
                var migrated = Copy();
                migrated.MigrateInputColors();
                migrated.MigrateSlotColors();
                foreach (var field in typeof(EliteDrumsColors).GetFields())
                {
                    if (field.FieldType != typeof(Color)) continue;
                    bool needsMigration = field.Name.Contains("Input") || field.Name.StartsWith("Fret", StringComparison.Ordinal);
                    string? source = GetLegacyJsonSource(field.Name);
                    bool Present(string name) => json.Properties().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (needsMigration && source != null && Present(source) && !Present(field.Name))
                        field.SetValue(this, field.GetValue(migrated));
                }
            }
        }

        /// <summary>
        /// Adapts Elite drums colors to the generic fret color provider interface using an
        /// encoded index space separate from stored saved-slot indexes: 0 is the kick bar,
        /// and 1..10 encode saved slots 0..9 via <see cref="EncodeSlot"/>. All three methods
        /// accept that same encoded space, so generic callers can pass one index everywhere.
        /// Particle colors are receptor/idle colors only: the bar uses the kick input
        /// effect color, mapped slots use their resolved primary role's input effect color,
        /// and unmapped slots use a deterministic neutral. Per-hit identity effects remain
        /// the separate identity APIs (<see cref="EliteDrumsColors.GetInputEffectColor"/>).
        /// </summary>
        public sealed class EliteDrumsFretColorAdapter : IFretColorProvider
        {
            public const int BAR_INDEX = 0;
            public const int FIRST_SLOT_INDEX = 1;

            private readonly EliteDrumsColors _colors;
            private readonly IReadOnlyDictionary<int, EliteDrumsColorRole>? _slotRoles;

            /// <param name="colors">Color source; must not be null.</param>
            /// <param name="slotRoles">Optional encoded-slot-index (1..10) to resolved
            /// primary role mapping supplied at runtime; null or a missing entry yields the
            /// neutral particle color for that slot.</param>
            public EliteDrumsFretColorAdapter(EliteDrumsColors colors,
                IReadOnlyDictionary<int, EliteDrumsColorRole>? slotRoles = null)
            {
                _colors = colors ?? throw new ArgumentNullException(nameof(colors));
                _slotRoles = slotRoles;
            }

            /// <summary>Encodes a ZERO-based saved slot (0..9) as an adapter index (1..10).</summary>
            public static int EncodeSlot(int savedSlot)
            {
                if (savedSlot < 0 || savedSlot >= EliteDrumsColors.SAVED_SLOT_COUNT)
                    throw new ArgumentOutOfRangeException(nameof(savedSlot));
                return FIRST_SLOT_INDEX + savedSlot;
            }

            public Color GetFretColor(int index) =>
                index == BAR_INDEX ? _colors.KickFret : _colors.GetSlotFretColor(index - FIRST_SLOT_INDEX);
            public Color GetFretInnerColor(int index) =>
                index == BAR_INDEX ? _colors.KickFretInner : _colors.GetSlotFretInnerColor(index - FIRST_SLOT_INDEX);

            public Color GetParticleColor(int index)
            {
                if (index == BAR_INDEX) return _colors.KickInputEffect;
                if (index < FIRST_SLOT_INDEX || index >= FIRST_SLOT_INDEX + EliteDrumsColors.SAVED_SLOT_COUNT)
                    throw new ArgumentOutOfRangeException(nameof(index));
                // Saved-slot styling never feeds particles; only a resolved primary role does.
                if (_slotRoles is not null && _slotRoles.TryGetValue(index, out var role))
                    return _colors.GetInputEffectColor(role);
                return DefaultWildcard;
            }
        }

        public sealed class EliteDrumsColorsJsonConverter : JsonConverter
        {
            public override bool CanWrite => false;
            public override bool CanConvert(Type objectType) => objectType == typeof(EliteDrumsColors);
            public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return new EliteDrumsColors();
                var json = JObject.Load(reader);
                var colors = new EliteDrumsColors();
                using (var input = json.CreateReader()) serializer.Populate(input, colors);
                colors.MigrateMissingJsonColors(json);
                return colors;
            }
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
                throw new NotSupportedException();
        }
    }
}
