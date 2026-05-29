using NUnit.Framework;
using YARG.Core;
using System.Linq;

namespace YARG.Core.UnitTests.Engine;

[TestFixture]
public sealed class InstrumentEnumsTests
{
    [Test]
    public void GameMode_PartyVocals_HasReservedByteValue()
    {
        Assert.AreEqual(16, (byte) GameMode.PartyVocals);
    }

    [Test]
    public void Instrument_PartyVocals_HasReservedByteValue()
    {
        Assert.AreEqual(42, (byte) Instrument.PartyVocals);
    }

    [Test]
    public void Instrument_PartyVocals_MapsToPartyVocalsGameMode()
    {
        Assert.AreEqual(GameMode.PartyVocals, Instrument.PartyVocals.ToNativeGameMode());
    }

    [Test]
    public void GameMode_PartyVocals_OnlyInstrumentIsPartyVocals()
    {
        var instruments = GameMode.PartyVocals.PossibleInstruments();
        Assert.AreEqual(1, instruments.Length);
        Assert.AreEqual(Instrument.PartyVocals, instruments[0]);
    }

    [Test]
    public void GameMode_Vocals_DoesNotIncludePartyVocals()
    {
        var instruments = GameMode.Vocals.PossibleInstruments();
        Assert.IsTrue(instruments.SequenceEqual(new[] { Instrument.Vocals, Instrument.Harmony }));
    }

    [Test]
    public void Profile_PartyVocalsInstrument_IsFreeVocals()
    {
        var profile = new YARG.Core.Game.YargProfile();
        profile.CurrentInstrument = Instrument.PartyVocals;
        Assert.IsTrue(profile.IsFreeVocals);
    }

    [Test]
    public void Profile_SoloVocals_IsNotFreeVocals()
    {
        var profile = new YARG.Core.Game.YargProfile();
        profile.CurrentInstrument = Instrument.Vocals;
        Assert.IsFalse(profile.IsFreeVocals);
    }
}
