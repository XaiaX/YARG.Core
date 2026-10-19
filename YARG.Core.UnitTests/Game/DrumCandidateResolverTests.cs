using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Core.UnitTests.Game;

public sealed class DrumCandidateResolverTests
{
    [TestCase(Instrument.FourLaneDrums, DrumSourceFormat.FourLane)]
    [TestCase(Instrument.ProDrums, DrumSourceFormat.FourLane)]
    [TestCase(Instrument.FiveLaneDrums, DrumSourceFormat.FiveLane)]
    [TestCase(Instrument.EliteDrums, DrumSourceFormat.Elite)]
    public void NativeSourceWins(Instrument output, DrumSourceFormat source)
    {
        var presence = new DrumSourcePresence(DifficultyMask.Easy, DifficultyMask.Easy,
            DifficultyMask.Easy, DifficultyMask.Easy);
        var candidate = DrumCandidateResolver.Resolve(presence, output, Difficulty.Beginner, true, true)!.Value;
        Assert.That(candidate.RequestedOutput, Is.EqualTo(output));
        Assert.That(candidate.SourceFormat, Is.EqualTo(source));
        Assert.That(candidate.BaseDifficulty, Is.EqualTo(Difficulty.Beginner));
        Assert.That(candidate.SourceDifficulty, Is.EqualTo(Difficulty.Easy));
        Assert.That(candidate.ScoreCategory, Is.EqualTo(ResolvedDrumPlayback.CategoryFor(output, source)));
    }

    [TestCase(false, DrumSourceFormat.FiveLane)]
    [TestCase(true, DrumSourceFormat.Elite)]
    public void ClassicFallbackRespectsPreference(bool preferElite, DrumSourceFormat source)
    {
        var presence = new DrumSourcePresence(DifficultyMask.None, DifficultyMask.Expert,
            DifficultyMask.Expert, DifficultyMask.Expert);
        Assert.That(DrumCandidateResolver.Resolve(presence, Instrument.ProDrums, Difficulty.Expert,
            false, preferElite)!.Value.SourceFormat, Is.EqualTo(source));
    }

    [Test]
    public void EliteRequiresOptInForClassicSourcesAndUsesFourLaneFirst()
    {
        var presence = new DrumSourcePresence(DifficultyMask.Hard, DifficultyMask.Hard,
            DifficultyMask.None, DifficultyMask.None);
        Assert.That(DrumCandidateResolver.Resolve(presence, Instrument.EliteDrums, Difficulty.Hard,
            false, false), Is.Null);
        var candidate = DrumCandidateResolver.Resolve(presence, Instrument.EliteDrums, Difficulty.Hard,
            true, false)!.Value;
        Assert.That(candidate.SourceFormat, Is.EqualTo(DrumSourceFormat.FourLane));
        Assert.That(candidate.ScoreCategory, Is.EqualTo(DrumScoreCategory.FourLaneDerivedElite));
    }

    [Test]
    public void EliteDownchartUsesExistingEligibilityMask()
    {
        var presence = new DrumSourcePresence(DifficultyMask.None, DifficultyMask.None,
            DifficultyMask.Hard | DifficultyMask.Expert, DifficultyMask.Hard);
        Assert.That(DrumCandidateResolver.Resolve(presence, Instrument.ProDrums, Difficulty.Expert,
            false, true), Is.Null);
        Assert.That(DrumCandidateResolver.Resolve(presence, Instrument.ProDrums, Difficulty.Hard,
            false, true), Is.Not.Null);
        Assert.That(DrumCandidateResolver.Resolve(presence, Instrument.EliteDrums, Difficulty.Expert,
            false, true), Is.Not.Null);
    }
}
