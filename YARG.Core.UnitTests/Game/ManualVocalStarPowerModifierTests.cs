using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Game;

[TestFixture]
public class ManualVocalStarPowerModifierTests
{
    [Test]
    public void ManualVocalStarPower_HasCorrectBitValue()
    {
        Assert.That((ulong) Modifier.ManualVocalStarPower, Is.EqualTo(1UL << 12));
    }

    [Test]
    public void PossibleModifiers_PartyVocals_IncludesManualVocalStarPower()
    {
        var (possible, excusable) = GameMode.PartyVocals.PossibleModifiers(Instrument.PartyVocals);
        Assert.That((possible & Modifier.ManualVocalStarPower) != 0, Is.True);
        Assert.That((excusable & Modifier.ManualVocalStarPower) == 0, Is.True);
    }

    [Test]
    public void PossibleModifiers_SoloVocals_IncludesManualVocalStarPower()
    {
        var (possible, excusable) = GameMode.Vocals.PossibleModifiers(Instrument.Vocals);
        Assert.That((possible & Modifier.ManualVocalStarPower) != 0, Is.True);
        Assert.That((excusable & Modifier.ManualVocalStarPower) == 0, Is.True);
    }

    [Test]
    public void PossibleModifiers_NonVocalMode_DoesNotIncludeManualVocalStarPower()
    {
        var (possible, _) = GameMode.FiveFretGuitar.PossibleModifiers(Instrument.FiveFretGuitar);
        Assert.That((possible & Modifier.ManualVocalStarPower) == 0, Is.True);
    }
}
