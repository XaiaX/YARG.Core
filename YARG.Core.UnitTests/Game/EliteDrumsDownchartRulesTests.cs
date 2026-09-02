using NUnit.Framework;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Song;
using YARG.Core.UnitTests.Song;

namespace YARG.Core.UnitTests.Game;

/// <summary>
/// Executable coverage for the shared "Elite (To …)" downchart target rules
/// (<see cref="EliteDrumsDownchartRules"/>). These are the predicates Difficulty
/// Select, Maestro, gameplay chart loading, and the replay analyzer all consult, so
/// their behavior is pinned here directly against constructed song entries rather
/// than through Unity-side source assertions.
/// </summary>
[TestFixture]
public sealed class EliteDrumsDownchartRulesTests
{
    private static readonly Instrument[] Targets =
    {
        Instrument.FourLaneDrums,
        Instrument.ProDrums,
        Instrument.FiveLaneDrums,
    };

    // ---- Target domain ----

    [Test]
    public void OnlyLaneDrumsFormatsAreValidTargets()
    {
        foreach (var target in Targets)
        {
            Assert.That(EliteDrumsDownchartRules.IsValidTarget(target), Is.True,
                $"{target} must be a valid downchart target");
        }
    }

    [Test]
    public void NonTargetInstrumentsAreInvalidTargets()
    {
        // Elite Drums is the *source* chart, not a downchart output; every other
        // instrument is outside the domain, as are out-of-range bytes that can come
        // from malformed or stale serialized values.
        var invalid = new[]
        {
            Instrument.EliteDrums,
            Instrument.FiveFretGuitar,
            Instrument.ProKeys,
            Instrument.Vocals,
            (Instrument) 99,
            (Instrument) 200,
        };

        foreach (var instrument in invalid)
        {
            Assert.That(EliteDrumsDownchartRules.IsValidTarget(instrument), Is.False,
                $"{instrument} must not be a valid downchart target");
        }
    }

    [Test]
    public void NullableOverloadAcceptsOnlyValidNonNullTargets()
    {
        Assert.That(EliteDrumsDownchartRules.IsValidTarget((Instrument?) null), Is.False,
            "no target is absent, not invalid-but-present");
        Assert.That(EliteDrumsDownchartRules.IsValidTarget((Instrument?) Instrument.ProDrums), Is.True);
        Assert.That(EliteDrumsDownchartRules.IsValidTarget((Instrument?) 99), Is.False);
    }

    // ---- Session playability (per song) ----

    [Test]
    public void EliteOnlySongIsPlayableForEveryTarget()
    {
        // A song with only an Elite Drums chart is playable for every target: the
        // chart is downcharted to the requested output format.
        var entry = CreateSong(elite: new[] { Difficulty.Easy, Difficulty.Expert });

        foreach (var target in Targets)
        {
            Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, target), Is.True,
                $"an elite-only song must be playable for target {target}");
        }
    }

    [Test]
    public void NativeFourLaneSongWithoutEliteIsStillPlayableForEveryTarget()
    {
        // The desired elite-free-library behavior: with no Elite Drums chart at all,
        // a usable native drums chart keeps the option playable — four-lane/Pro
        // natively, and five-lane through the 4 -> 5 conversion.
        var entry = CreateSong(pro: new[] { Difficulty.Easy }, fourLane: new[] { Difficulty.Easy });

        foreach (var target in Targets)
        {
            Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, target), Is.True,
                $"a native four-lane song must be playable for target {target}");
        }
    }

    [Test]
    public void SongWithNeitherEliteNorDrumsIsPlayableForNoTarget()
    {
        // The mixed-show hole these rules close: a song with neither an Elite Drums
        // chart nor any native drums chart must not be offered any target row.
        var entry = CreateSong(fiveFret: new[] { Difficulty.Easy });

        foreach (var target in Targets)
        {
            Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, target), Is.False,
                $"a song with neither elite nor native drums must not be playable for target {target}");
        }
    }

    [Test]
    public void MalformedTargetIsNeverPlayableAndNeverThrows()
    {
        // A garbage instrument value (malformed serialized data) must fail closed
        // instead of reaching the song part lookups, which would throw.
        var entry = CreateSong(elite: new[] { Difficulty.Expert });
        var garbage = (Instrument) 99;

        Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, garbage), Is.False);
        Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, garbage, Difficulty.Expert), Is.False);
    }

    // ---- Difficulty availability (per song) ----

    [Test]
    public void EliteSongDifficultiesComeFromTheGeneratedDownchart()
    {
        var entry = CreateSong(elite: new[] { Difficulty.Easy, Difficulty.Expert });

        using (Assert.EnterMultipleScope())
        {
            // Beginner is synthesized from Easy by the downchart loader.
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Beginner), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FourLaneDrums, Difficulty.Easy), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FiveLaneDrums, Difficulty.Expert), Is.True);

            // The elite chart has no Medium/Hard/ExpertPlus, so those are unavailable —
            // and the song's (empty) native drums charts must not be consulted.
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Medium), Is.False);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Hard), Is.False);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.ExpertPlus), Is.False);
        }
    }

    [Test]
    public void EliteLessSongKeepsNativeDifficultiesIncludingConversions()
    {
        // A five-lane-only song keeps its native difficulties for four-lane/Pro
        // targets through the 5 -> 4 conversion, exactly like native selection.
        var entry = CreateSong(fiveLane: new[] { Difficulty.Medium, Difficulty.Hard });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FourLaneDrums, Difficulty.Medium), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Hard), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FiveLaneDrums, Difficulty.Hard), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FourLaneDrums, Difficulty.Easy), Is.False);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FiveLaneDrums, Difficulty.Easy), Is.False);
        }
    }

    [Test]
    public void DownchartDifficultiesAreTheGeneratedOnesNotTheEliteOnes()
    {
        // The generated downchart can be a strict subset of the Elite chart's
        // difficulties (e.g. Easy downcharts but Expert is all hat pedals). The rules
        // must follow the generated downchart, because that is the only track the
        // loader will build.
        var entry = CreateSong(
            elite: new[] { Difficulty.Easy, Difficulty.Expert },
            eliteDownchart: new[] { Difficulty.Easy });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Beginner), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Easy), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FourLaneDrums, Difficulty.Expert), Is.False,
                "an Elite difficulty with an empty generated downchart must not be offered");
        }
    }

    // ---- Degenerate Elite charts: active chart, no playable generated notes ----

    [Test]
    public void EliteOnlySongWithEmptyGeneratedDownchartIsPlayableForNoTarget()
    {
        // The regression case from review: an Elite chart whose every note is dropped
        // by the downchart builder (unforced/invisible hat pedals only) generates no
        // downchart at all — MoonSongLoader.DownchartEliteDrumsTrack returns null.
        // With no native drums either, chart presence alone must NOT make the song
        // playable for a target: the rules must agree with the loader's non-empty
        // generated-downchart notion.
        var entry = CreateSong(
            elite: new[] { Difficulty.Easy, Difficulty.Expert },
            eliteDownchart: System.Array.Empty<Difficulty>());

        foreach (var target in Targets)
        {
            Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, target), Is.False,
                $"an elite-only song with an empty generated downchart must not be playable for target {target}");
        }

        foreach (var difficulty in new[] { Difficulty.Beginner, Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert, Difficulty.ExpertPlus })
        {
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, difficulty), Is.False,
                $"no difficulty may be offered when the generated downchart is empty ({difficulty})");
        }
    }

    [Test]
    public void EliteSongWithEmptyGeneratedDownchartFallsBackToNative()
    {
        // Same degenerate Elite chart, but the song also has a native five-lane chart:
        // the loader falls back to native, so the rules must too — playable for every
        // target through the 5 -> 4 conversions, with the NATIVE difficulties.
        var entry = CreateSong(
            elite: new[] { Difficulty.Easy, Difficulty.Expert },
            eliteDownchart: System.Array.Empty<Difficulty>(),
            fiveLane: new[] { Difficulty.Medium, Difficulty.Hard });

        using (Assert.EnterMultipleScope())
        {
            foreach (var target in Targets)
            {
                Assert.That(EliteDrumsDownchartRules.IsSongPlayableForTarget(entry, target), Is.True,
                    $"native fallback must keep the song playable for target {target}");
            }

            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FourLaneDrums, Difficulty.Medium), Is.True,
                "difficulties come from the native chart when no downchart is generated");
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.ProDrums, Difficulty.Hard), Is.True);
            Assert.That(EliteDrumsDownchartRules.HasTargetDifficulty(entry, Instrument.FiveLaneDrums, Difficulty.Easy), Is.False,
                "the elite chart's Easy must not leak into the offered difficulties");
        }
    }

    // ---- Profile-consistency guard ----

    [Test]
    public void DownchartTargetIsActiveOnlyWhenConsistentWithTheProfile()
    {
        static YargProfile Profile(Instrument instrument, GameMode gameMode, Instrument? target) => new(Guid.NewGuid())
        {
            CurrentInstrument = instrument,
            GameMode = gameMode,
            EliteDrumsDownchartTarget = target,
        };

        using (Assert.EnterMultipleScope())
        {
            // Fully consistent: target is well-formed, equals the current instrument,
            // and the game mode is a supported drum mode.
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(
                Profile(Instrument.FiveLaneDrums, GameMode.FiveLaneDrums, Instrument.FiveLaneDrums)), Is.True);
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(
                Profile(Instrument.ProDrums, GameMode.EliteDrums, Instrument.ProDrums)), Is.True);
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(
                Profile(Instrument.FourLaneDrums, GameMode.FourLaneDrums, Instrument.FourLaneDrums)), Is.True);

            // Absent target: inactive, not invalid.
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(
                Profile(Instrument.FourLaneDrums, GameMode.FourLaneDrums, null)), Is.False);

            // Well-formed domain, but stale/corrupted relative to the profile.
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(
                Profile(Instrument.FourLaneDrums, GameMode.FourLaneDrums, Instrument.FiveLaneDrums)), Is.False,
                "a target that no longer equals CurrentInstrument must fall back native");

            // Malformed values outside the target domain.
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(
                Profile((Instrument) 99, GameMode.FourLaneDrums, (Instrument) 99)), Is.False);

            // A drum target on a non-drum game mode (or instrument) is inconsistent.
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(
                Profile(Instrument.FourLaneDrums, GameMode.FiveFretGuitar, Instrument.FourLaneDrums)), Is.False);
            Assert.That(EliteDrumsDownchartRules.IsDownchartTargetActive(
                Profile(Instrument.FiveFretGuitar, GameMode.FiveFretGuitar, Instrument.FiveFretGuitar)), Is.False,
                "a valid-domain target is never active for a guitar profile");
        }
    }

    /// <summary>
    /// Builds a song entry from raw part/difficulty descriptions. Parts that are not
    /// mentioned stay at their inactive defaults.
    /// </summary>
    private static TestSongEntry CreateSong(
        Difficulty[]? elite = null,
        Difficulty[]? eliteDownchart = null,
        Difficulty[]? pro = null,
        Difficulty[]? fourLane = null,
        Difficulty[]? fiveLane = null,
        Difficulty[]? fiveFret = null)
    {
        var parts = AvailableParts.Default;
        foreach (var difficulty in elite ?? System.Array.Empty<Difficulty>()) parts.EliteDrums.ActivateDifficulty(difficulty);
        // The generated downchart defaults to the Elite chart's own difficulties (every
        // note converts); an explicit eliteDownchart — including empty — models a chart
        // whose Elite difficulties generate fewer (or no) playable downchart notes.
        foreach (var difficulty in (eliteDownchart ?? elite) ?? System.Array.Empty<Difficulty>()) parts.EliteDrumsDownchart.ActivateDifficulty(difficulty);
        foreach (var difficulty in pro ?? System.Array.Empty<Difficulty>()) parts.ProDrums.ActivateDifficulty(difficulty);
        foreach (var difficulty in fourLane ?? System.Array.Empty<Difficulty>()) parts.FourLaneDrums.ActivateDifficulty(difficulty);
        foreach (var difficulty in fiveLane ?? System.Array.Empty<Difficulty>()) parts.FiveLaneDrums.ActivateDifficulty(difficulty);
        foreach (var difficulty in fiveFret ?? System.Array.Empty<Difficulty>()) parts.FiveFretGuitar.ActivateDifficulty(difficulty);

        var entry = new TestSongEntry();
        entry.SetParts(parts);
        return entry;
    }
}
