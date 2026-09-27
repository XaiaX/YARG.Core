using System.Linq;
using NUnit.Framework;
using YARG.Core.Audio;

namespace YARG.Core.UnitTests.Audio;

public class EliteDrumsStemTests
{
    [Test]
    public void NativeEliteUsesTheSameDrumStemsAsOtherNativeDrumInstruments()
    {
        var expected = Instrument.ProDrums.ToSongStems().ToArray();
        Assert.That(expected, Is.Not.Empty);
        Assert.That(Instrument.EliteDrums.ToSongStems(), Is.EqualTo(expected));
        Assert.That(Instrument.FourLaneDrums.ToSongStems(), Is.EqualTo(expected));
        Assert.That(Instrument.FiveLaneDrums.ToSongStems(), Is.EqualTo(expected));
    }
}
