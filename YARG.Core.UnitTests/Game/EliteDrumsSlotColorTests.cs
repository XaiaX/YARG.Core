using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Game;

public class EliteDrumsSlotColorTests
{
    private static readonly Color[] Pattern =
    {
        ColorProfile.DefaultRed, ColorProfile.DefaultYellow, ColorProfile.DefaultBlue,
        ColorProfile.DefaultOrange, ColorProfile.DefaultGreen
    };

    [Test]
    public void SavedSlotAccessorsAreZeroBasedWithRepeatingDefaults()
    {
        var colors = new ColorProfile.EliteDrumsColors();
        for (int slot = 0; slot < ColorProfile.EliteDrumsColors.SAVED_SLOT_COUNT; slot++)
        {
            Assert.That(colors.GetSlotFretColor(slot), Is.EqualTo(Pattern[slot % Pattern.Length]), $"outer {slot}");
            Assert.That(colors.GetSlotFretInnerColor(slot), Is.EqualTo(Pattern[slot % Pattern.Length]), $"inner {slot}");
        }
        // DoubleKick styling is retained for its real identity, not as a slot default.
        Assert.That(colors.DoubleKickFret, Is.EqualTo(ColorProfile.DefaultSilverFret));
        Assert.That(colors.DoubleKickFretInner, Is.EqualTo(ColorProfile.DefaultSilverFret));
        Assert.Throws<ArgumentOutOfRangeException>(() => colors.GetSlotFretColor(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => colors.GetSlotFretColor(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => colors.GetSlotFretInnerColor(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => colors.GetSlotFretInnerColor(10));
    }

    [Test]
    public void JsonMigratesOnlyFiveLegacyPositionsToSlotsZeroThroughFour()
    {
        var colors = JsonConvert.DeserializeObject<ColorProfile.EliteDrumsColors>(
            "{\"SnareFret\":\"Aqua\",\"HatFret\":\"Brown\",\"LeftCrashTom1Fret\":\"LawnGreen\"," +
            "\"RideTom2Fret\":\"Chocolate\",\"RightCrashTom3Fret\":\"DimGray\"," +
            "\"SnareFretInner\":\"Pink\",\"SnareParticles\":\"Lime\"," +
            "\"DoubleKickFret\":\"Silver\",\"DoubleKickFretInner\":\"Silver\"," +
            "\"Fret3\":\"Blue\",\"Fret7\":\"Red\"}")!;
        Assert.That(colors.Fret1.ToArgb(), Is.EqualTo(Color.Aqua.ToArgb()));
        Assert.That(colors.Fret2.ToArgb(), Is.EqualTo(Color.Brown.ToArgb()));
        // Fret3 was supplied explicitly (equal to its default), so no migration occurs.
        Assert.That(colors.Fret3.ToArgb(), Is.EqualTo(Color.Blue.ToArgb()));
        Assert.That(colors.Fret4.ToArgb(), Is.EqualTo(Color.Chocolate.ToArgb()));
        Assert.That(colors.Fret5.ToArgb(), Is.EqualTo(Color.DimGray.ToArgb()));
        Assert.That(colors.Fret1Inner.ToArgb(), Is.EqualTo(Color.Pink.ToArgb()));
        // DoubleKick styling never fills saved slot 5 (Fret6) in JSON or defaults.
        Assert.That(colors.Fret6.ToArgb(), Is.EqualTo(ColorProfile.DefaultRed.ToArgb()));
        Assert.That(colors.Fret6Inner.ToArgb(), Is.EqualTo(ColorProfile.DefaultRed.ToArgb()));
        Assert.That(colors.DoubleKickFret.ToArgb(), Is.EqualTo(Color.Silver.ToArgb()));
        Assert.That(colors.DoubleKickFretInner.ToArgb(), Is.EqualTo(Color.Silver.ToArgb()));
        Assert.That(colors.Fret7.ToArgb(), Is.EqualTo(Color.Red.ToArgb()));
        Assert.That(colors.SnareInputFret.ToArgb(), Is.EqualTo(Color.Aqua.ToArgb()));
        Assert.That(colors.SnareInputEffect.ToArgb(), Is.EqualTo(Color.Lime.ToArgb()));
    }

    [TestCase(3, 58)]
    [TestCase(4, 90)]
    public void OldBinaryMigratesFivePositionsOnlyAndKeepsRecordBoundary(int version, int colorCount)
    {
        var source = new ColorProfile.EliteDrumsColors
        {
            SnareFret = Color.Aqua,
            SnareFretInner = Color.Pink,
            RightCrashTom3Fret = Color.DimGray,
            DoubleKickFret = Color.Silver,
            DoubleKickFretInner = Color.Silver
        };
        using var current = new MemoryStream();
        using (var writer = new BinaryWriter(current, System.Text.Encoding.UTF8, true)) source.Serialize(writer);
        using var old = new MemoryStream();
        old.Write(current.ToArray(), 0, colorCount * 4);
        using (var writer = new BinaryWriter(old, System.Text.Encoding.UTF8, true)) writer.Write(123456);
        old.Position = 0;
        using var reader = new BinaryReader(old);
        var result = new ColorProfile.EliteDrumsColors();
        result.Deserialize(reader, version);
        Assert.That(result.Fret1.ToArgb(), Is.EqualTo(Color.Aqua.ToArgb()));
        Assert.That(result.Fret1Inner.ToArgb(), Is.EqualTo(Color.Pink.ToArgb()));
        Assert.That(result.Fret5.ToArgb(), Is.EqualTo(Color.DimGray.ToArgb()));
        // No DoubleKick -> Fret6 binary migration; slots 5..9 keep defaults.
        Assert.That(result.Fret6.ToArgb(), Is.EqualTo(ColorProfile.DefaultRed.ToArgb()));
        Assert.That(result.Fret6Inner.ToArgb(), Is.EqualTo(ColorProfile.DefaultRed.ToArgb()));
        Assert.That(result.DoubleKickFret.ToArgb(), Is.EqualTo(Color.Silver.ToArgb()));
        Assert.That(reader.ReadInt32(), Is.EqualTo(123456));
    }

    [Test]
    public void VersionFiveRoundTripsAllSlots()
    {
        var source = new ColorProfile("slots");
        source.EliteDrums.Fret6 = Color.Aqua;
        source.EliteDrums.Fret10Inner = Color.Pink;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) source.Serialize(writer);
        stream.Position = 0;
        var result = new ColorProfile("read");
        using var reader = new BinaryReader(stream);
        result.Deserialize(reader);
        Assert.That(result.Version, Is.EqualTo(5));
        Assert.That(result.EliteDrums.GetSlotFretColor(5).ToArgb(), Is.EqualTo(Color.Aqua.ToArgb()));
        Assert.That(result.EliteDrums.GetSlotFretInnerColor(9).ToArgb(), Is.EqualTo(Color.Pink.ToArgb()));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [Test]
    public void SlotsAreIndependentOfKickIdentityAndCopies()
    {
        var source = new ColorProfile("slots");
        source.EliteDrums.Fret1 = Color.Aqua;
        source.EliteDrums.Fret10Inner = Color.Pink;
        var copy = (ColorProfile) source.CopyWithNewName("copy");
        copy.EliteDrums.Fret1 = Color.Black;
        Assert.That(source.EliteDrums.GetSlotFretColor(0), Is.EqualTo(Color.Aqua));
        Assert.That(copy.EliteDrums.GetSlotFretInnerColor(9), Is.EqualTo(Color.Pink));
        // Legacy kick-bar provider is untouched and separate from slot 0.
        Assert.That(source.EliteDrums.GetFretColor(0), Is.EqualTo(source.EliteDrums.KickFret));
        Assert.That(source.EliteDrums.SnareInputFret, Is.EqualTo(ColorProfile.DefaultRed));
    }

    [Test]
    public void AdapterSeparatesKickBarIndexFromFirstSlot()
    {
        var colors = new ColorProfile.EliteDrumsColors
        {
            KickFret = Color.Aqua,
            KickFretInner = Color.Pink,
            Fret1 = Color.Lime,
            Fret1Inner = Color.Orange
        };
        var adapter = new ColorProfile.EliteDrumsFretColorAdapter(colors);
        Assert.That(adapter.GetFretColor(ColorProfile.EliteDrumsFretColorAdapter.BAR_INDEX), Is.EqualTo(Color.Aqua));
        Assert.That(adapter.GetFretInnerColor(ColorProfile.EliteDrumsFretColorAdapter.BAR_INDEX), Is.EqualTo(Color.Pink));
        Assert.That(adapter.GetFretColor(ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(0)), Is.EqualTo(Color.Lime));
        Assert.That(adapter.GetFretInnerColor(ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(0)), Is.EqualTo(Color.Orange));
        colors.Fret1 = Color.Black;
        Assert.That(adapter.GetFretColor(0), Is.EqualTo(Color.Aqua));
        Assert.That(adapter.GetFretInnerColor(0), Is.EqualTo(Color.Pink));
        colors.KickFret = Color.White;
        colors.KickFretInner = Color.DimGray;
        Assert.That(adapter.GetFretColor(1), Is.EqualTo(Color.Black));
        Assert.That(adapter.GetFretInnerColor(1), Is.EqualTo(Color.Orange));
    }

    [Test]
    public void AdapterEncodesSavedSlotsForFretAndInnerColors()
    {
        var colors = new ColorProfile.EliteDrumsColors();
        var adapter = new ColorProfile.EliteDrumsFretColorAdapter(colors);
        for (int slot = 0; slot < ColorProfile.EliteDrumsColors.SAVED_SLOT_COUNT; slot++)
        {
            int encoded = ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(slot);
            Assert.That(adapter.GetFretColor(encoded), Is.EqualTo(colors.GetSlotFretColor(slot)), $"outer {slot}");
            Assert.That(adapter.GetFretInnerColor(encoded), Is.EqualTo(colors.GetSlotFretInnerColor(slot)), $"inner {slot}");
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.GetFretColor(11));
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.GetFretInnerColor(-1));
    }

    [Test]
    public void AdapterRejectsNullColorsAndInvalidSavedSlots()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ColorProfile.EliteDrumsFretColorAdapter(null!));
        Assert.That(ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(0), Is.EqualTo(1));
        Assert.That(ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(9), Is.EqualTo(10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(10));
    }

    [Test]
    public void AdapterParticlesUseEncodedSpaceWithNeutralWithoutMapping()
    {
        var colors = new ColorProfile.EliteDrumsColors();
        var adapter = new ColorProfile.EliteDrumsFretColorAdapter(colors);
        Assert.That(adapter.GetParticleColor(0), Is.EqualTo(colors.KickInputEffect));
        for (int slot = 0; slot < ColorProfile.EliteDrumsColors.SAVED_SLOT_COUNT; slot++)
            Assert.That(adapter.GetParticleColor(ColorProfile.EliteDrumsFretColorAdapter.EncodeSlot(slot)),
                Is.EqualTo(ColorProfile.DefaultWildcard), $"slot {slot}");
        // Saved-slot idle colors never feed particles.
        colors.Fret1 = Color.Aqua;
        colors.Fret1Inner = Color.Pink;
        colors.Fret10 = Color.Lime;
        Assert.That(adapter.GetParticleColor(1), Is.EqualTo(ColorProfile.DefaultWildcard));
        Assert.That(adapter.GetParticleColor(10), Is.EqualTo(ColorProfile.DefaultWildcard));
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.GetParticleColor(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.GetParticleColor(11));
    }

    [Test]
    public void AdapterParticlesUseSuppliedPrimaryRoleInputEffects()
    {
        var colors = new ColorProfile.EliteDrumsColors();
        var roles = new Dictionary<int, EliteDrumsColorRole>
        {
            [9] = EliteDrumsColorRole.Snare,
            [1] = EliteDrumsColorRole.Tom2
        };
        var adapter = new ColorProfile.EliteDrumsFretColorAdapter(colors, roles);
        Assert.That(adapter.GetParticleColor(9), Is.EqualTo(colors.SnareInputEffect));
        Assert.That(adapter.GetParticleColor(1), Is.EqualTo(colors.Tom2InputEffect));
        Assert.That(adapter.GetParticleColor(0), Is.EqualTo(colors.KickInputEffect));
        // Slots without a mapped primary role stay neutral.
        Assert.That(adapter.GetParticleColor(5), Is.EqualTo(ColorProfile.DefaultWildcard));
        // Role input effects drive particles; saved slot idle colors do not.
        colors.SnareInputEffect = Color.Lime;
        colors.Fret9Inner = Color.Pink;
        Assert.That(adapter.GetParticleColor(9), Is.EqualTo(Color.Lime));
    }
}
