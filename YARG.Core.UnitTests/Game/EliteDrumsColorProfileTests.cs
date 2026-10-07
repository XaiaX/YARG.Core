using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Game;

public class EliteDrumsColorProfileTests
{
    private static readonly FieldInfo[] ColorFields = typeof(ColorProfile.EliteDrumsColors)
        .GetFields().Where(field => field.FieldType == typeof(Color)).ToArray();

    private static void AssertColors(ColorProfile.EliteDrumsColors actual, ColorProfile.EliteDrumsColors expected)
    {
        foreach (var field in ColorFields)
            Assert.That(((Color) field.GetValue(actual)!).ToArgb(),
                Is.EqualTo(((Color) field.GetValue(expected)!).ToArgb()), field.Name);
    }

    [TestCase("{}")]
    [TestCase("{\"EliteDrums\":null}")]
    [TestCase("{\"EliteDrums\":{}}")]
    public void Json_MissingNullOrEmptySectionUsesCanonicalDefaults(string json)
    {
        var result = JsonConvert.DeserializeObject<ColorProfile>(json)!;
        Assert.That(result.EliteDrums, Is.Not.Null);
        AssertColors(result.EliteDrums, new ColorProfile.EliteDrumsColors());
    }

    [Test]
    public void Json_PartialSectionRetainsOtherDefaultsAndRoundTripsAllFields()
    {
        var result = JsonConvert.DeserializeObject<ColorProfile>("{\"EliteDrums\":{\"SnareNote\":\"Blue\"}}")!;
        var expected = new ColorProfile.EliteDrumsColors { SnareNote = Color.Blue };
        AssertColors(result.EliteDrums, expected);
        SetDistinctColors(result.EliteDrums);
        AssertColors(JsonConvert.DeserializeObject<ColorProfile>(JsonConvert.SerializeObject(result))!.EliteDrums,
            result.EliteDrums);
    }

    [Test]
    public void CanonicalDefaults_PreserveNativeRoleAndPositionColors()
    {
        var colors = new ColorProfile.EliteDrumsColors();
        var expectedNotes = new[]
        {
            ColorProfile.DefaultRed, ColorProfile.DefaultYellowCymbal, ColorProfile.DefaultYellowCymbal,
            ColorProfile.DefaultYellowCymbal, ColorProfile.DefaultBlueCymbal, ColorProfile.DefaultOrange,
            ColorProfile.DefaultGreenCymbal, ColorProfile.DefaultBlue, ColorProfile.DefaultOrange,
            ColorProfile.DefaultGreen, ColorProfile.DefaultPurple, ColorProfile.DefaultSilver,
            ColorProfile.DefaultSilver, ColorProfile.DefaultOrange, ColorProfile.DefaultOrange,
            ColorProfile.DefaultRed, ColorProfile.DefaultWildcard
        };
        foreach (var role in Enum.GetValues<EliteDrumsColorRole>())
        {
            Assert.That(colors.GetNoteColor(role), Is.EqualTo(expectedNotes[(int) role]), role.ToString());
            Assert.That(colors.GetNoteStarPowerColor(role), Is.EqualTo(role == EliteDrumsColorRole.Wildcard
                ? ColorProfile.DefaultWildcardStarpower : ColorProfile.DefaultStarpower));
        }
        var hands = new[] { ColorProfile.DefaultRed, ColorProfile.DefaultYellow, ColorProfile.DefaultBlue,
            ColorProfile.DefaultOrange, ColorProfile.DefaultGreen };
        for (int i = 0; i < hands.Length; i++)
        {
            Assert.That(colors.GetFretColor(i + 1), Is.EqualTo(hands[i]));
            Assert.That(colors.GetFretInnerColor(i + 1), Is.EqualTo(hands[i]));
            Assert.That(colors.GetParticleColor(i + 1), Is.EqualTo(hands[i]));
        }
        var kick = new ColorProfile.FiveLaneDrumsColors();
        Assert.That(colors.KickFret, Is.EqualTo(kick.KickFret));
        Assert.That(colors.KickFretInner, Is.EqualTo(kick.KickFretInner));
        Assert.That(colors.KickParticles, Is.EqualTo(kick.KickParticles));
        Assert.That(colors.DoubleKickFret, Is.EqualTo(kick.DoubleKickFret));
        Assert.That(colors.DoubleKickFretInner, Is.EqualTo(kick.DoubleKickFretInner));
        Assert.That(colors.DoubleKickParticles, Is.EqualTo(kick.DoubleKickParticles));
        Assert.That(colors.Miss, Is.EqualTo(ColorProfile.DefaultMiss));
        Assert.That(colors.Metal, Is.EqualTo(ColorProfile.DefaultMetal));
        Assert.That(colors.MetalStarPower, Is.EqualTo(ColorProfile.DefaultMetalStarPower));
    }

    [Test]
    public void CopyAndPresets_AreIndependentOfClassicColors()
    {
        var source = new ColorProfile("source");
        source.FourLaneDrums.RedDrum = Color.Black;
        source.FiveLaneDrums.KickNote = Color.White;
        AssertColors(source.EliteDrums, new ColorProfile.EliteDrumsColors());
        SetDistinctColors(source.EliteDrums);
        var copy = (ColorProfile) source.CopyWithNewName("copy");
        AssertColors(copy.EliteDrums, source.EliteDrums);
        Assert.That(copy.EliteDrums, Is.Not.SameAs(source.EliteDrums));
        copy.EliteDrums.SnareNote = Color.Pink;
        Assert.That(source.EliteDrums.SnareNote, Is.Not.EqualTo(copy.EliteDrums.SnareNote));
        foreach (var preset in new[] { ColorProfile.Default, ColorProfile.CircularDefault, ColorProfile.AprilFoolsDefault })
            AssertColors(preset.EliteDrums, new ColorProfile.EliteDrumsColors());
        Assert.That(ColorProfile.Default.EliteDrums, Is.Not.SameAs(ColorProfile.CircularDefault.EliteDrums));
    }

    private static void SetDistinctColors(ColorProfile.EliteDrumsColors colors)
    {
        for (int i = 0; i < ColorFields.Length; i++)
            ColorFields[i].SetValue(colors, Color.FromArgb(255, i + 1, i + 2, i + 3));
    }

    [Test]
    public void Binary_V3AppendsAfterUnchangedClassicPayloadAndUsesCurrentHeader()
    {
        var source = new ColorProfile("source") { Version = 1 };
        SetDistinctColors(source.EliteDrums);
        source.ProKeys.BlackNote = Color.Pink;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        source.Serialize(writer);
        var end = stream.Position;
        writer.Write(0x12345678);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        Assert.That(reader.ReadInt32(), Is.EqualTo(4));
        Assert.That(reader.ReadString(), Is.EqualTo(source.Name));
        var classic = new ColorProfile("classic");
        classic.FiveFretGuitar.Deserialize(reader, 3);
        classic.SixFretGuitar.Deserialize(reader, 3);
        classic.FourLaneDrums.Deserialize(reader, 3);
        classic.FiveLaneDrums.Deserialize(reader, 3);
        classic.ProKeys.Deserialize(reader, 3);
        Assert.That(classic.ProKeys.BlackNote.ToArgb(), Is.EqualTo(Color.Pink.ToArgb()));
        Assert.That(end - stream.Position, Is.EqualTo(ColorFields.Length * sizeof(int)));
        stream.Position = 0;
        var restored = new ColorProfile("restored");
        restored.Deserialize(reader);
        AssertColors(restored.EliteDrums, source.EliteDrums);
        Assert.That(stream.Position, Is.EqualTo(end));
        Assert.That(reader.ReadInt32(), Is.EqualTo(0x12345678));
    }

    [Test]
    public void Binary_V3KeepsRecordBoundaryAndMigratesMergedInputColors()
    {
        var source = new ColorProfile("v3");
        source.EliteDrums.LeftCrashTom1Fret = Color.Pink;
        source.EliteDrums.LeftCrashTom1Particles = Color.Blue;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(3);
        writer.Write(source.Name);
        source.FiveFretGuitar.Serialize(writer);
        source.SixFretGuitar.Serialize(writer);
        source.FourLaneDrums.Serialize(writer);
        source.FiveLaneDrums.Serialize(writer);
        source.ProKeys.Serialize(writer);
        using var elite = new MemoryStream();
        using (var eliteWriter = new BinaryWriter(elite, Encoding.UTF8, true))
            source.EliteDrums.Serialize(eliteWriter);
        writer.Write(elite.ToArray(), 0, (int) elite.Length - 32 * sizeof(int));
        long boundary = stream.Position;
        writer.Write(0x12345678);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        var restored = new ColorProfile("restored");
        restored.Deserialize(reader);
        Assert.That(stream.Position, Is.EqualTo(boundary));
        Assert.That(reader.ReadInt32(), Is.EqualTo(0x12345678));
        Assert.That(restored.EliteDrums.LeftCrashInputFret.ToArgb(), Is.EqualTo(Color.Pink.ToArgb()));
        Assert.That(restored.EliteDrums.Tom1InputFret.ToArgb(), Is.EqualTo(Color.Pink.ToArgb()));
        Assert.That(restored.EliteDrums.LeftCrashInputEffect.ToArgb(), Is.EqualTo(Color.Blue.ToArgb()));
        Assert.That(restored.EliteDrums.Tom1InputEffect.ToArgb(), Is.EqualTo(Color.Blue.ToArgb()));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void Binary_LegacyLayoutsDefaultEliteAndLeaveFollowingRecordUntouched(int version)
    {
        var source = new ColorProfile("legacy");
        source.ProKeys.BlackNote = Color.Pink;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(version);
        writer.Write(source.Name);
        WriteLegacySection(writer, source.FiveFretGuitar.Serialize, version == 1 ? 12 : 0);
        source.SixFretGuitar.Serialize(writer);
        WriteLegacySection(writer, source.FourLaneDrums.Serialize, version == 1 ? 4 : 0);
        WriteLegacySection(writer, source.FiveLaneDrums.Serialize, version == 1 ? 4 : 0);
        source.ProKeys.Serialize(writer);
        var end = stream.Position;
        writer.Write(0x12345678);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        var restored = new ColorProfile("restored");
        SetDistinctColors(restored.EliteDrums);
        restored.Deserialize(reader);
        Assert.That(stream.Position, Is.EqualTo(end));
        Assert.That(reader.ReadInt32(), Is.EqualTo(0x12345678));
        Assert.That(restored.ProKeys.BlackNote.ToArgb(), Is.EqualTo(Color.Pink.ToArgb()));
        AssertColors(restored.EliteDrums, new ColorProfile.EliteDrumsColors());
        using var upgraded = new MemoryStream();
        using var upgradedWriter = new BinaryWriter(upgraded);
        restored.Serialize(upgradedWriter);
        upgraded.Position = 0;
        using var upgradedReader = new BinaryReader(upgraded);
        Assert.That(upgradedReader.ReadInt32(), Is.EqualTo(4));
    }

    // V1 lacked only these known V2 tails; preserve the original section order and ProKeys boundary.
    private static void WriteLegacySection(BinaryWriter writer, Action<BinaryWriter> serialize, int tailBytes)
    {
        using var section = new MemoryStream();
        using var sectionWriter = new BinaryWriter(section);
        serialize(sectionWriter);
        writer.Write(section.ToArray(), 0, (int) section.Length - tailBytes);
    }

    [Test]
    public void Providers_CoverAllRolesAndFixedPositions()
    {
        var colors = new ColorProfile.EliteDrumsColors();
        SetDistinctColors(colors);
        foreach (var role in Enum.GetValues<EliteDrumsColorRole>())
        {
            Assert.That(colors.GetNoteColor(role), Is.EqualTo(role == EliteDrumsColorRole.Wildcard
                ? ColorProfile.DefaultWildcard : ColorFields.Single(f => f.Name == role + "Note").GetValue(colors)));
            Assert.That(colors.GetNoteStarPowerColor(role), Is.EqualTo(role == EliteDrumsColorRole.Wildcard
                ? ColorProfile.DefaultWildcardStarpower : ColorFields.Single(f => f.Name == role + "Starpower").GetValue(colors)));
            if (role == EliteDrumsColorRole.Wildcard) continue;
            Assert.That(colors.GetInputFretColor(role), Is.EqualTo(ColorFields.Single(f => f.Name == role + "InputFret").GetValue(colors)));
            Assert.That(colors.GetInputEffectColor(role), Is.EqualTo(ColorFields.Single(f => f.Name == role + "InputEffect").GetValue(colors)));
        }
        foreach (var fret in Enum.GetValues<ColorProfile.EliteDrumsFret>())
        {
            Assert.That(colors.GetFretColor((int) fret), Is.EqualTo(ColorFields.Single(f => f.Name == fret + "Fret").GetValue(colors)));
            Assert.That(colors.GetFretInnerColor((int) fret), Is.EqualTo(ColorFields.Single(f => f.Name == fret + "FretInner").GetValue(colors)));
            Assert.That(colors.GetParticleColor((int) fret), Is.EqualTo(ColorFields.Single(f => f.Name == fret + "Particles").GetValue(colors)));
        }
        Assert.That(colors.GetMetalColor(false), Is.EqualTo(colors.Metal));
        Assert.That(colors.GetMetalColor(true), Is.EqualTo(colors.MetalStarPower));
    }

    [Test]
    public void PhysicalInputEffectsRemainIndependentOfMergedPositionAndWildcardFields()
    {
        var colors = new ColorProfile.EliteDrumsColors
        {
            LeftCrashInputEffect = Color.Red, Tom1InputEffect = Color.Blue,
            LeftCrashTom1Particles = Color.Green, WildcardNote = Color.Black,
            WildcardStarpower = Color.Black
        };
        Assert.That(colors.GetInputEffectColor(EliteDrumsColorRoles.GetInputRole(EliteDrumsAction.EliteLeftCrash)), Is.EqualTo(Color.Red));
        Assert.That(colors.GetInputEffectColor(EliteDrumsColorRoles.GetInputRole(EliteDrumsAction.EliteTom1)), Is.EqualTo(Color.Blue));
        Assert.That(colors.GetParticleColor((int) ColorProfile.EliteDrumsFret.LeftCrashTom1), Is.EqualTo(Color.Green));
        Assert.That(colors.GetNoteColor(EliteDrumsColorRole.Wildcard), Is.EqualTo(ColorProfile.DefaultWildcard));
        Assert.That(colors.GetNoteStarPowerColor(EliteDrumsColorRole.Wildcard), Is.EqualTo(ColorProfile.DefaultWildcardStarpower));
    }

    private static EliteDrumNote Note(EliteDrumPad pad, EliteDrumsHatState state = EliteDrumsHatState.Indifferent,
        EliteDrumsHatPedalType pedal = EliteDrumsHatPedalType.Stomp, bool flam = false, bool doubleKick = false)
        => new(pad, DrumNoteType.Neutral, state, pedal, flam, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumsChannelFlag.None, 0, 0, doubleKick);

    [Test]
    public void RoleMapping_HandlesAllHandsHatStatesPedalsAndFlams()
    {
        foreach (var pad in new[] { EliteDrumPad.Snare, EliteDrumPad.LeftCrash, EliteDrumPad.Ride,
                     EliteDrumPad.RightCrash, EliteDrumPad.Tom1, EliteDrumPad.Tom2, EliteDrumPad.Tom3 })
        {
            var role = Enum.Parse<EliteDrumsColorRole>(pad.ToString());
            Assert.That(EliteDrumsColorRoles.GetRole(Note(pad)), Is.EqualTo(role));
            Assert.That(EliteDrumsColorRoles.GetRole(Note(pad, flam: true)), Is.EqualTo(EliteDrumsColorRole.HandFlam));
            Assert.That(EliteDrumsColorRoles.GetRole(Note(pad, flam: true), true), Is.EqualTo(role));
        }
        foreach (var state in Enum.GetValues<EliteDrumsHatState>())
            Assert.That(EliteDrumsColorRoles.GetRole(Note(EliteDrumPad.HiHat, state)),
                Is.EqualTo(Enum.Parse<EliteDrumsColorRole>("Hat" + state)));
        Assert.That(EliteDrumsColorRoles.GetRole(Note(EliteDrumPad.Kick)), Is.EqualTo(EliteDrumsColorRole.Kick));
        Assert.That(EliteDrumsColorRoles.GetRole(Note(EliteDrumPad.Kick, doubleKick: true)), Is.EqualTo(EliteDrumsColorRole.DoubleKick));
        Assert.That(EliteDrumsColorRoles.GetRole(Note(EliteDrumPad.Kick, flam: true, doubleKick: true)), Is.EqualTo(EliteDrumsColorRole.KickFlam));
        Assert.That(EliteDrumsColorRoles.GetRole(Note(EliteDrumPad.HatPedal)), Is.EqualTo(EliteDrumsColorRole.Stomp));
        Assert.That(EliteDrumsColorRoles.GetRole(Note(EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.Splash)), Is.EqualTo(EliteDrumsColorRole.Splash));
        Assert.Throws<ArgumentException>(() => EliteDrumsColorRoles.GetRole(Note(EliteDrumPad.HatPedal, pedal: EliteDrumsHatPedalType.InvisibleTerminator)));
    }
}
