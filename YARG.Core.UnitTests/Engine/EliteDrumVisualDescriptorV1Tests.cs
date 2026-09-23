using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteDrumVisualDescriptorV1Tests
{
    [Test]
    public void Builder_MergesEnabledComponentsAndUsesExplicitAppearanceOwner()
    {
        var descriptor = EliteDrumComponentVisualDescriptorBuilder.Build("lane-1", new[]
        {
            new EliteDrumComponentMetadata("tom-1", EliteDrumComponentKind.Drum, 2, "tom", isAppearanceOwner: false),
            new EliteDrumComponentMetadata("crash-l", EliteDrumComponentKind.Cymbal, 7, "crash", isAppearanceOwner: true),
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(descriptor.Enabled, Is.True);
            Assert.That(descriptor.Components.Select(component => component.Id), Is.EqualTo(new[] { "tom-1", "crash-l" }));
            Assert.That(descriptor.AppearanceOwnerId, Is.EqualTo("crash-l"));
            Assert.That(descriptor.AppearanceColorSlot, Is.EqualTo(7));
            Assert.That(descriptor.AppearanceModelKey, Is.EqualTo("crash"));
        }
    }

    [Test]
    public void Builder_ExcludesDisabledMembersWithoutChangingEnabledOrder()
    {
        var descriptor = EliteDrumComponentVisualDescriptorBuilder.Build("lane-2", new[]
        {
            new EliteDrumComponentMetadata("disabled", EliteDrumComponentKind.Cymbal, 9, "disabled", enabled: false),
            new EliteDrumComponentMetadata("snare", EliteDrumComponentKind.Drum, 1, "snare"),
            new EliteDrumComponentMetadata("ride", EliteDrumComponentKind.Cymbal, 4, "ride"),
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(descriptor.Enabled, Is.True);
            Assert.That(descriptor.Components.Select(component => component.Id), Is.EqualTo(new[] { "snare", "ride" }));
            Assert.That(descriptor.AppearanceOwnerId, Is.EqualTo("snare"));
        }
    }

    [Test]
    public void Builder_RequiresAuthoredHandLaneAndExcludesOrdinaryKickAndPedal()
    {
        var ordinaryTom = Origin("ordinary-tom", EliteDrumPad.Tom1, 0);
        var eligibleSnare = Origin("eligible-snare", EliteDrumPad.Snare, 20);
        var kick = Origin("kick", EliteDrumPad.Kick, 40);
        var pedal = Origin("pedal", EliteDrumPad.HatPedal, 60);
        var eligibleTom = Origin("eligible-tom", EliteDrumPad.Tom2, 80);
        var eligibleCymbal = Origin("eligible-cymbal", EliteDrumPad.Ride, 100);
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(Note(ordinaryTom, FourLaneDrumPad.RedDrum, 0));
        difficulty.Notes.Add(Note(eligibleSnare, FourLaneDrumPad.RedDrum, 20, NoteFlags.Tremolo));
        difficulty.Notes.Add(Note(kick, FourLaneDrumPad.Kick, 40, NoteFlags.Tremolo));
        difficulty.Notes.Add(Note(pedal, FourLaneDrumPad.YellowDrum, 60, NoteFlags.Tremolo));
        difficulty.Notes.Add(Note(eligibleTom, FourLaneDrumPad.BlueDrum, 80, NoteFlags.Tremolo));
        difficulty.Notes.Add(Note(eligibleCymbal, FourLaneDrumPad.BlueCymbal, 100, NoteFlags.Tremolo));
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>());

        var descriptors = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);

        Assert.That(descriptors, Has.Count.EqualTo(3));
        Assert.That(descriptors.SelectMany(descriptor => descriptor.SourceIds),
            Is.EquivalentTo(new[] { "eligible-snare", "eligible-tom", "eligible-cymbal" }));
    }

    [Test]
    public void Builder_EmitsOneDescriptorPerMergedComponentAndUnionsPhysicalInterval()
    {
        var firstSource = new EliteDrumSourceDefinition("repeat-a", 0, (int)EliteDrumPad.Tom1,
            0, 10, 1.0, 0.25);
        var secondSource = new EliteDrumSourceDefinition("repeat-b", 1, (int)EliteDrumPad.Tom1,
            5, 10, 2.0, 0.25);
        var firstOrigin = new EliteDrumConversionOrigin(firstSource);
        var secondOrigin = new EliteDrumConversionOrigin(secondSource);
        var target = new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int)FourLaneDrumPad.RedDrum);
        var ledger = new EliteDrumConversionLedger(
            EliteDrumFinalPadComponentBuilder.Build(new[]
            {
                new EliteDrumSourceMembership(firstOrigin, target, 0, 15),
                new EliteDrumSourceMembership(secondOrigin, target, 5, 15),
            }), Array.Empty<EliteDrumDroppedOrigin>());
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(new DrumNote((int)FourLaneDrumPad.RedDrum, DrumNoteType.Neutral,
            DrumNoteFlags.None, NoteFlags.Tremolo, 1.0, 0, conversionOrigin: firstOrigin));
        difficulty.Notes.Add(new DrumNote((int)FourLaneDrumPad.RedDrum, DrumNoteType.Neutral,
            DrumNoteFlags.None, NoteFlags.Tremolo, 2.0, 5, conversionOrigin: secondOrigin));

        var descriptors = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(descriptors, Has.Count.EqualTo(1));
            Assert.That(descriptors[0].SourceIds, Is.EqualTo(new[] { "repeat-a", "repeat-b" }));
            Assert.That(descriptors[0].FirstPhysicalEventTime, Is.EqualTo(1.0));
            Assert.That(descriptors[0].LastPhysicalEventTime, Is.EqualTo(2.25));
            Assert.That(descriptors[0].LastPhysicalEventTime, Is.GreaterThan(descriptors[0].FirstPhysicalEventTime));
        }
    }

    [Test]
    public void Builder_MixedCollisionEmitsOnlySurvivingEligibleSourcesAndKeepsFlamOrigins()
    {
        var eligibleSource = new EliteDrumSourceDefinition("tom", 0, (int)EliteDrumPad.Tom1, 0, 10, 1.0, 0.25);
        var eligibleOrigin = new EliteDrumConversionOrigin(eligibleSource);
        var flamOrigin = new EliteDrumConversionOrigin(eligibleSource, 1);
        var snareOrigin = new EliteDrumConversionOrigin(
            new EliteDrumSourceDefinition("snare", 1, (int)EliteDrumPad.Snare, 0, 10, 1.0, 0.25));
        var target = new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int)FourLaneDrumPad.RedDrum);
        var ledger = new EliteDrumConversionLedger(
            EliteDrumFinalPadComponentBuilder.Build(new[]
            {
                new EliteDrumSourceMembership(eligibleOrigin, target, 0, 10),
                new EliteDrumSourceMembership(flamOrigin, target, 0, 10),
                new EliteDrumSourceMembership(snareOrigin, target, 0, 10),
            }), Array.Empty<EliteDrumDroppedOrigin>());
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(Note(eligibleOrigin, FourLaneDrumPad.RedDrum, 0, NoteFlags.Tremolo));
        difficulty.Notes.Add(Note(flamOrigin, FourLaneDrumPad.RedDrum, 0, NoteFlags.None));
        difficulty.Notes.Add(Note(snareOrigin, FourLaneDrumPad.RedDrum, 0, NoteFlags.None));

        var descriptors = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(descriptors, Has.Count.EqualTo(1));
            Assert.That(descriptors[0].SourceIds, Is.EqualTo(new[] { "tom" }));
            Assert.That(descriptors[0].Origins, Is.EquivalentTo(new[] { eligibleOrigin, flamOrigin }));
            Assert.That(descriptors[0].FirstPhysicalEventTime, Is.EqualTo(1.0));
            Assert.That(descriptors[0].LastPhysicalEventTime, Is.EqualTo(1.25));
        }
    }

    [Test]
    public void Builder_DoesNotCreateDescriptorForComponentWithoutSurvivingSourceNote()
    {
        var source = Origin("dropped", EliteDrumPad.Tom1, 0);
        var target = new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int)FourLaneDrumPad.RedDrum);
        var ledger = new EliteDrumConversionLedger(
            EliteDrumFinalPadComponentBuilder.Build(new[]
            {
                new EliteDrumSourceMembership(source, target, 0, 10),
            }), Array.Empty<EliteDrumDroppedOrigin>());
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);

        Assert.That(EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger), Is.Empty);
    }

    [Test]
    public void Builder_EmitsSeparateDescriptorsForDisjointComponentsOnSameLane()
    {
        var firstSource = new EliteDrumSourceDefinition("first", 0, (int)EliteDrumPad.Tom1,
            0, 4, 1.0, 0.1);
        var secondSource = new EliteDrumSourceDefinition("second", 1, (int)EliteDrumPad.Tom1,
            10, 4, 3.0, 0.1);
        var firstOrigin = new EliteDrumConversionOrigin(firstSource);
        var secondOrigin = new EliteDrumConversionOrigin(secondSource);
        var target = new EliteDrumFinalPadIdentity(Instrument.FiveLaneDrums, (int)FiveLaneDrumPad.Red);
        var ledger = new EliteDrumConversionLedger(
            EliteDrumFinalPadComponentBuilder.Build(new[]
            {
                new EliteDrumSourceMembership(firstOrigin, target, 0, 4),
                new EliteDrumSourceMembership(secondOrigin, target, 10, 14),
            }), Array.Empty<EliteDrumDroppedOrigin>());
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.FiveLaneDrums, Difficulty.Expert);
        difficulty.Notes.Add(new DrumNote((int)FiveLaneDrumPad.Red, DrumNoteType.Neutral,
            DrumNoteFlags.None, NoteFlags.Tremolo, 1.0, 0, conversionOrigin: firstOrigin));
        difficulty.Notes.Add(new DrumNote((int)FiveLaneDrumPad.Red, DrumNoteType.Neutral,
            DrumNoteFlags.None, NoteFlags.Tremolo, 3.0, 10, conversionOrigin: secondOrigin));

        var descriptors = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);

        Assert.That(descriptors, Has.Count.EqualTo(2));
        Assert.That(descriptors.Select(descriptor => descriptor.SourceIds.Single()),
            Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void Builder_ReturnsDisabledDescriptorWhenAllMembersAreDisabled()
    {
        var descriptor = EliteDrumComponentVisualDescriptorBuilder.Build("lane-disabled", new[]
        {
            new EliteDrumComponentMetadata("tom", EliteDrumComponentKind.Drum, 2, "tom", enabled: false, isAppearanceOwner: true),
            new EliteDrumComponentMetadata("ride", EliteDrumComponentKind.Cymbal, 4, "ride", enabled: false),
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(descriptor.Enabled, Is.False);
            Assert.That(descriptor.Components, Is.Empty);
            Assert.That(descriptor.AppearanceOwnerId, Is.Empty);
            Assert.That(descriptor.AppearanceModelKey, Is.Empty);
        }
    }

    [Test]
    public void AuthoredRecords_ResolveSingleFinalPadIdentityFromFinalChildrenAndPublish()
    {
        var sourceA = new EliteDrumSourceDefinition("a", 0, (int)EliteDrumPad.Snare, 0, 10, 1.0, 0.25);
        var sourceB = new EliteDrumSourceDefinition("b", 1, (int)EliteDrumPad.Snare, 5, 10, 2.0, 0.25);
        var sourceC = new EliteDrumSourceDefinition("c", 2, (int)EliteDrumPad.Snare, 8, 10, 3.0, 0.25);
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(), new[]
            {
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_SnareLane, 0, 10,
                    new[] { sourceA, sourceB, sourceC }),
            });
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceA), FourLaneDrumPad.RedDrum, 0));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceB), FourLaneDrumPad.RedDrum, 5));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceC), FourLaneDrumPad.RedDrum, 8));

        var records = EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger);
        var published = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);

        using (Assert.EnterMultipleScope())
        {
            // Stage 2: the survivors' actual final children agree on one final pad, so
            // the record resolves to that true final identity and publishes.
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].GameplayId,
                Is.EqualTo("ProDrums:authored-lane:EliteDrums_SnareLane:0:10"));
            var expectedFinalPad = new EliteDrumFinalPadIdentity(Instrument.ProDrums,
                (int) FourLaneDrumPad.RedDrum);
            Assert.That(records[0].FinalPad, Is.EqualTo(expectedFinalPad));
            Assert.That(records[0].IsFinalIdentityResolved, Is.True);

            // The published descriptor is the resolved record with the appearance derived
            // strictly from the true final identity: the authored Elite pad never leaks
            // into the published appearance (the authored Snare slot is 2, RedDrum is 1).
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].FinalPad, Is.EqualTo(expectedFinalPad));
            Assert.That(published[0].IsFinalIdentityResolved, Is.True);
            Assert.That(published[0].VisualIdentity.AppearanceColorSlot,
                Is.EqualTo((int) FourLaneDrumPad.RedDrum));
            Assert.That(published[0].VisualIdentity.AppearanceModelKey,
                Is.EqualTo(nameof(FourLaneDrumPad.RedDrum)));
            Assert.That(published[0].VisualIdentity.AppearanceOwnerId,
                Is.EqualTo("ProDrums:pad:1"));

            // The provenance record keeps the authored lane visual for diagnostics.
            Assert.That(records[0].VisualIdentity.AppearanceColorSlot, Is.EqualTo((int)EliteDrumPad.Snare));
            Assert.That(records[0].VisualIdentity.AppearanceModelKey, Is.EqualTo(nameof(EliteDrumPad.Snare)));
            Assert.That(records[0].AuthoredStartTick, Is.EqualTo(0u));
            Assert.That(records[0].AuthoredEndTick, Is.EqualTo(10u));
            Assert.That(records[0].SourceIds, Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(records[0].FirstPhysicalEventTime, Is.EqualTo(1.0));
            Assert.That(records[0].LastPhysicalEventTime, Is.EqualTo(3.25));
        }
    }

    [Test]
    public void DescriptorContract_ResolvesKickIdentityAndKeepsItDistinctFromUnresolved()
    {
        // Pad 0 is the valid final Kick pad; a descriptor carrying it as a resolved
        // identity must not look unresolved.
        var resolvedKick = new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int)FourLaneDrumPad.Kick);
        var visual = new EliteDrumComponentVisualDescriptor("kick-lane", true, "kick", 0, "kick",
            Array.Empty<EliteDrumComponentMetadata>());
        var resolved = new EliteDrumVisualDescriptorV1("ProDrums:0:0:10", new[] { "kick" },
            new[] { new EliteDrumConversionOrigin(new EliteDrumSourceDefinition("kick", 0,
                (int)EliteDrumPad.Kick, 0, 10)) }, resolvedKick, visual, 0, 10, 1.0, 1.25, 1.0, 1.25,
            true, false);
        var unresolved = new EliteDrumVisualDescriptorV1("ProDrums:authored-lane:0:10", new[] { "a" },
            new[] { new EliteDrumConversionOrigin(new EliteDrumSourceDefinition("a", 0,
                (int)EliteDrumPad.Snare, 0, 10)) }, EliteDrumFinalPadIdentity.UnresolvedFor(Instrument.ProDrums),
            visual, 0, 10, 1.0, 1.25, 1.0, 1.25, true, false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.FinalPad.IsResolved, Is.True);
            Assert.That(resolved.IsFinalIdentityResolved, Is.True);
            Assert.That(resolved.FinalPad.IsUnresolved, Is.False);
            // Identical pad value, different explicit state: not the same identity.
            Assert.That(unresolved.FinalPad, Is.Not.EqualTo(resolved.FinalPad));
            Assert.That(unresolved.IsFinalIdentityResolved, Is.False);
        }
    }

    [Test]
    public void AuthoredBuilder_BuildsNothingWhenFewerThanThreeMembersSurvive()
    {
        var sourceA = new EliteDrumSourceDefinition("a", 0, (int)EliteDrumPad.Snare, 0, 10);
        var sourceB = new EliteDrumSourceDefinition("b", 1, (int)EliteDrumPad.Snare, 5, 10);
        var sourceC = new EliteDrumSourceDefinition("c", 2, (int)EliteDrumPad.Snare, 8, 10);
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(), new[]
            {
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_Tom1Lane, 0, 10,
                    new[] { sourceA, sourceB, sourceC }),
            });
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        // Only two of three authored members survive conversion.
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceA), FourLaneDrumPad.YellowDrum, 0));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceB), FourLaneDrumPad.YellowDrum, 5));

        using (Assert.EnterMultipleScope())
        {
            // A malformed phrase is neither published nor preserved as a record; the
            // provenance stays on the surviving notes for diagnostics.
            Assert.That(EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger), Is.Empty);
            Assert.That(EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger),
                Is.Empty);
        }
    }

    [Test]
    public void AuthoredBuilder_NeverCountsKickOrPedalMembers()
    {
        var snareA = new EliteDrumSourceDefinition("snare-a", 0, (int)EliteDrumPad.Snare, 0, 10);
        var snareB = new EliteDrumSourceDefinition("snare-b", 1, (int)EliteDrumPad.Snare, 5, 10);
        var kick = new EliteDrumSourceDefinition("kick", 2, (int)EliteDrumPad.Kick, 7, 10);
        var pedal = new EliteDrumSourceDefinition("pedal", 3, (int)EliteDrumPad.HatPedal, 8, 10);
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(), new[]
            {
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_SnareLane, 0, 10,
                    new[] { snareA, snareB, kick, pedal }),
            });
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(snareA), FourLaneDrumPad.RedDrum, 0));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(snareB), FourLaneDrumPad.RedDrum, 5));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(kick), FourLaneDrumPad.Kick, 7));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(pedal), FourLaneDrumPad.YellowDrum, 8));

        // Two hand members plus kick/pedal occurrences is still below the gate, and
        // the kick/pedal sources never leak into a record or descriptor.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger), Is.Empty);
            Assert.That(EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger),
                Is.Empty);
        }
    }

    [Test]
    public void AuthoredBuilder_TouchingPhrasesStaySeparateInstances()
    {
        var sourceA = new EliteDrumSourceDefinition("a", 0, (int)EliteDrumPad.Tom1, 0, 4);
        var sourceB = new EliteDrumSourceDefinition("b", 1, (int)EliteDrumPad.Tom1, 1, 4);
        var sourceC = new EliteDrumSourceDefinition("c", 2, (int)EliteDrumPad.Tom1, 2, 4);
        var sourceD = new EliteDrumSourceDefinition("d", 3, (int)EliteDrumPad.Tom1, 4, 4);
        var sourceE = new EliteDrumSourceDefinition("e", 4, (int)EliteDrumPad.Tom1, 5, 4);
        var sourceF = new EliteDrumSourceDefinition("f", 5, (int)EliteDrumPad.Tom1, 6, 4);
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(), new[]
            {
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_Tom1Lane, 0, 4,
                    new[] { sourceA, sourceB, sourceC }),
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_Tom1Lane, 4, 8,
                    new[] { sourceD, sourceE, sourceF }),
            });
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        foreach (var (source, tick) in new[] { (sourceA, 0u), (sourceB, 1u), (sourceC, 2u), (sourceD, 4u), (sourceE, 5u), (sourceF, 6u) })
        {
            difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(source), FourLaneDrumPad.YellowDrum, tick));
        }

        var records = EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger);
        var published = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);
        var expectedFinalPad = new EliteDrumFinalPadIdentity(Instrument.ProDrums,
            (int) FourLaneDrumPad.YellowDrum);

        using (Assert.EnterMultipleScope())
        {
            // Both phrases resolve and publish independently: they are separate instances.
            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(published.All(descriptor => descriptor.FinalPad.Equals(expectedFinalPad)), Is.True);
            Assert.That(published.All(descriptor => descriptor.IsFinalIdentityResolved), Is.True);
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records[0].AuthoredStartTick, Is.EqualTo(0u));
            Assert.That(records[0].AuthoredEndTick, Is.EqualTo(4u));
            Assert.That(records[0].SourceIds, Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(records[1].AuthoredStartTick, Is.EqualTo(4u));
            Assert.That(records[1].AuthoredEndTick, Is.EqualTo(8u));
            Assert.That(records[1].SourceIds, Is.EqualTo(new[] { "d", "e", "f" }));
        }
    }

    [Test]
    public void AuthoredBuilder_FlamExpansionsCountAsSeparatePhysicalMembers()
    {
        var sourceA = new EliteDrumSourceDefinition("flam", 0, (int)EliteDrumPad.Snare, 0, 10, 1.0, 0.25);
        var sourceB = new EliteDrumSourceDefinition("plain", 1, (int)EliteDrumPad.Snare, 5, 10, 2.0, 0.25);
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(), new[]
            {
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_SnareLane, 0, 10,
                    new[] { sourceA, sourceB }),
            });
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceA), FourLaneDrumPad.RedDrum, 0, NoteFlags.Tremolo));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceA, 1), FourLaneDrumPad.YellowDrum, 0));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceB), FourLaneDrumPad.RedDrum, 5));

        var records = EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger);

        using (Assert.EnterMultipleScope())
        {
            // Two authored gems expand to three physical members: the phrase passes the
            // surviving-member gate (flams count individually).
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].SourceIds, Is.EqualTo(new[] { "flam", "plain" }));
            Assert.That(records[0].Origins, Has.Count.EqualTo(3));
            Assert.That(records[0].LastPhysicalEventTime, Is.EqualTo(2.25));

            // Stage 2: the flam expansion lands on a second final pad, so the survivors
            // split into RedDrum x2 and YellowDrum x1. The split is not supported by
            // membership (no group reaches three members), so the whole phrase is
            // malformed: it stays explicitly unresolved and publishes nothing — the
            // majority pad is never chosen arbitrarily.
            Assert.That(records[0].FinalPad.IsUnresolved, Is.True);
            Assert.That(records[0].IsFinalIdentityResolved, Is.False);
            Assert.That(EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger), Is.Empty);
        }
    }

    [Test]
    public void AuthoredBuilder_ResolvesFinalPadFromActualFinalChildrenForEveryAuthoredHandLane()
    {
        var phraseTypes = new[]
        {
            PhraseType.EliteDrums_SnareLane, PhraseType.EliteDrums_HiHatLane,
            PhraseType.EliteDrums_LeftCrashLane, PhraseType.EliteDrums_Tom1Lane,
            PhraseType.EliteDrums_Tom2Lane, PhraseType.EliteDrums_Tom3Lane,
            PhraseType.EliteDrums_RideLane, PhraseType.EliteDrums_RightCrashLane,
        };
        // The final children carry FourLaneDrumPad.YellowDrum in every fixture: the
        // resolved identity must be that ACTUAL final pad for every authored lane type,
        // never the authored Elite pad of the lane (which differs per type).
        var expectedFinalPad = new EliteDrumFinalPadIdentity(Instrument.ProDrums,
            (int) FourLaneDrumPad.YellowDrum);

        foreach (var phraseType in phraseTypes)
        {
            var (difficulty, ledger) = AuthoredLaneFixture(phraseType);

            var records = EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger);
            var published = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(records, Has.Count.EqualTo(1), $"phrase {phraseType}");
                Assert.That(records[0].FinalPad, Is.EqualTo(expectedFinalPad), $"phrase {phraseType}");
                Assert.That(records[0].FinalPad.IsResolved, Is.True, $"phrase {phraseType}");
                Assert.That(records[0].IsFinalIdentityResolved, Is.True, $"phrase {phraseType}");

                Assert.That(published, Has.Count.EqualTo(1), $"phrase {phraseType}");
                Assert.That(published[0].FinalPad, Is.EqualTo(expectedFinalPad), $"phrase {phraseType}");
                // The published appearance names the final pad, never the authored Elite
                // pad (pad values can coincide numerically, so compare the model key).
                Assert.That(published[0].VisualIdentity.AppearanceModelKey,
                    Is.Not.EqualTo(EliteDrumAuthoredLanePhraseTypes.ToAuthoredPad(phraseType).ToString()),
                    $"authored pad leaked for {phraseType}");
                Assert.That(published[0].VisualIdentity.AppearanceColorSlot,
                    Is.EqualTo((int) FourLaneDrumPad.YellowDrum), $"phrase {phraseType}");

                // Authored membership and gate data stay available on the record.
                Assert.That(records[0].SourceIds,
                    Is.EqualTo(new[] { "a", "b", "c" }), $"phrase {phraseType}");
                Assert.That(records[0].Origins, Has.Count.EqualTo(3), $"phrase {phraseType}");
                Assert.That(records[0].RulesetEligible, Is.True, $"phrase {phraseType}");
            }
        }
    }

    [Test]
    public void AuthoredBuilder_OffersOnlyTrueFinalIdentitiesToAppearanceResolvers()
    {
        // A resolved record: the resolver is invoked exactly once with the TRUE final
        // identity from the actual final children — never the authored Elite pad and
        // never an unresolved identity.
        var (difficulty, ledger) = AuthoredLaneFixture(PhraseType.EliteDrums_SnareLane);
        var offeredPads = new List<EliteDrumFinalPadIdentity>();

        var published = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger, finalPad =>
        {
            offeredPads.Add(finalPad);
            return new EliteDrumComponentVisualDescriptor("resolved-appearance", true, "resolved-owner", 9,
                "resolved-model", Array.Empty<EliteDrumComponentMetadata>());
        });

        using (Assert.EnterMultipleScope())
        {
            var expectedFinalPad = new EliteDrumFinalPadIdentity(Instrument.ProDrums,
                (int) FourLaneDrumPad.YellowDrum);
            Assert.That(offeredPads, Is.EqualTo(new[] { expectedFinalPad }));
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].VisualIdentity.LaneId, Is.EqualTo("resolved-appearance"));
            Assert.That(published[0].VisualIdentity.AppearanceColorSlot, Is.EqualTo(9));
        }

        // A malformed mixed record: it is never published, so no resolver call can ever
        // receive its unresolved identity.
        var (mixedDifficulty, mixedLedger) = MixedFinalPadFixture(
            (FourLaneDrumPad.RedDrum, 3), (FourLaneDrumPad.BlueCymbal, 1));
        var malformedPads = new List<EliteDrumFinalPadIdentity>();

        var malformedPublished = EliteDrumVisualDescriptorV1Builder.Build(mixedDifficulty, mixedLedger,
            finalPad =>
            {
                malformedPads.Add(finalPad);
                return new EliteDrumComponentVisualDescriptor("should-not-happen", true, "owner", 9,
                    "model", Array.Empty<EliteDrumComponentMetadata>());
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(malformedPublished, Is.Empty);
            Assert.That(malformedPads, Is.Empty);

            var records = EliteDrumVisualDescriptorV1Builder
                .BuildAuthoredLanePhraseRecords(mixedDifficulty, mixedLedger);
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].FinalPad.IsUnresolved, Is.True);
            Assert.That(records[0].VisualIdentity.LaneId,
                Is.EqualTo("authored-lane:EliteDrums_SnareLane"));
            Assert.That(records[0].VisualIdentity.AppearanceOwnerId,
                Is.EqualTo(nameof(PhraseType.EliteDrums_SnareLane)));
        }
    }

    [Test]
    public void AuthoredRecords_PassPublishedSelectionGatesExactlyWhenFinalIdentityResolves()
    {
        // Mirrors the three published Unity/V1 selection gates over Core descriptors.
        // Stage 2: resolved authored records pass every gate like any resolved
        // descriptor; malformed records (unresolved final identity) fail all of them.
        // 1. Descriptor publish (DrumsPlayer.SpawnEliteVisualDescriptors): publishes only
        //    when RulesetEligible && !CodaExcluded && resolved final identity.
        // 2. Visual spawn eligibility (EliteDrumVisualAdapter.IsPermanentlyEligible):
        //    eligible only when RulesetEligible && !CodaExcluded && VisualIdentity.Enabled
        //    && resolved final identity.
        // 3. Adapter-owned lane suppression / V1 ownership (IsAdapterOwnedGeneratedLane):
        //    owns only when spawn-eligible && Origins.Contains(origin).
        static bool Publishes(EliteDrumVisualDescriptorV1 descriptor) =>
            descriptor.RulesetEligible && !descriptor.CodaExcluded && descriptor.IsFinalIdentityResolved;
        static bool SpawnEligible(EliteDrumVisualDescriptorV1 descriptor) =>
            Publishes(descriptor) && descriptor.VisualIdentity.Enabled;
        static bool OwnsLane(EliteDrumVisualDescriptorV1 descriptor, EliteDrumConversionOrigin origin) =>
            SpawnEligible(descriptor) && descriptor.Origins.Contains(origin);

        var (difficulty, ledger) = AuthoredLaneFixture(PhraseType.EliteDrums_SnareLane);
        var authored = EliteDrumVisualDescriptorV1Builder
            .BuildAuthoredLanePhraseRecords(difficulty, ledger).Single();
        var (mixedDifficulty, mixedLedger) = MixedFinalPadFixture(
            (FourLaneDrumPad.RedDrum, 3), (FourLaneDrumPad.BlueCymbal, 1));
        var malformed = EliteDrumVisualDescriptorV1Builder
            .BuildAuthoredLanePhraseRecords(mixedDifficulty, mixedLedger).Single();
        var resolved = ResolvedPadDerivedDescriptor();

        using (Assert.EnterMultipleScope())
        {
            // The resolved authored record passes every gate and is part of the
            // published output.
            Assert.That(authored.FinalPad.IsResolved, Is.True);
            Assert.That(Publishes(authored), Is.True);
            Assert.That(SpawnEligible(authored), Is.True);
            Assert.That(OwnsLane(authored, authored.Origins[0]), Is.True);
            Assert.That(EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger),
                Has.Count.EqualTo(1));

            // The malformed mixed record keeps provenance but fails every gate and is
            // never part of the published output.
            Assert.That(malformed.FinalPad.IsUnresolved, Is.True);
            Assert.That(Publishes(malformed), Is.False);
            Assert.That(SpawnEligible(malformed), Is.False);
            Assert.That(OwnsLane(malformed, malformed.Origins[0]), Is.False);
            Assert.That(EliteDrumVisualDescriptorV1Builder.Build(mixedDifficulty, mixedLedger),
                Is.Empty);

            // Control: a pad-derived resolved final-pad descriptor passes the same gates.
            Assert.That(resolved.FinalPad.IsResolved, Is.True);
            Assert.That(Publishes(resolved), Is.True);
            Assert.That(SpawnEligible(resolved), Is.True);
            Assert.That(OwnsLane(resolved, resolved.Origins[0]), Is.True);
        }
    }

    [Test]
    public void Builder_MixedAuthoredAndUnrelatedResolvedPadDescriptorsBothBehaveCorrectly()
    {
        // A difficulty carrying BOTH a valid authored hand-lane phrase AND unrelated
        // resolved pad-derived components: the pad-derived path must not be suppressed
        // by the authored data, and the authored members must not be duplicated into
        // pad-derived descriptors.
        var sourceA = new EliteDrumSourceDefinition("a", 0, (int)EliteDrumPad.Snare, 0, 10, 1.0, 0.25);
        var sourceB = new EliteDrumSourceDefinition("b", 1, (int)EliteDrumPad.Snare, 5, 10, 2.0, 0.25);
        var sourceC = new EliteDrumSourceDefinition("c", 2, (int)EliteDrumPad.Snare, 8, 10, 3.0, 0.25);
        var phrase = new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_SnareLane, 0, 10,
            new[] { sourceA, sourceB, sourceC });
        var originA = new EliteDrumConversionOrigin(sourceA);
        var originB = new EliteDrumConversionOrigin(sourceB);
        var originC = new EliteDrumConversionOrigin(sourceC);

        var tomSource = new EliteDrumSourceDefinition("unrelated-tom", 3, (int)EliteDrumPad.Tom1, 2, 10, 1.5, 0.25);
        var tomOrigin = new EliteDrumConversionOrigin(tomSource);
        var tom2Source = new EliteDrumSourceDefinition("unrelated-tom-2", 4, (int)EliteDrumPad.Tom2, 20, 10, 4.0, 0.25);
        var tom2Origin = new EliteDrumConversionOrigin(tom2Source);

        var redTarget = new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int)FourLaneDrumPad.RedDrum);
        var blueTarget = new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int)FourLaneDrumPad.BlueDrum);
        var ledger = new EliteDrumConversionLedger(
            EliteDrumFinalPadComponentBuilder.Build(new[]
            {
                // The merged red component covers both the unrelated tom and the authored
                // member "a"; the authored origin must be partitioned out.
                new EliteDrumSourceMembership(tomOrigin, redTarget, 0, 10),
                new EliteDrumSourceMembership(originA, redTarget, 0, 10),
                new EliteDrumSourceMembership(tom2Origin, blueTarget, 20, 30),
            }), Array.Empty<EliteDrumDroppedOrigin>(), new[] { phrase });

        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        // Member "a" is even tremolo-marked: only the authored claim keeps it out of the
        // red component's hand-lane descriptor.
        difficulty.Notes.Add(Note(originA, FourLaneDrumPad.RedDrum, 0, NoteFlags.Tremolo));
        difficulty.Notes.Add(Note(originB, FourLaneDrumPad.RedDrum, 5));
        difficulty.Notes.Add(Note(originC, FourLaneDrumPad.RedDrum, 8));
        difficulty.Notes.Add(Note(tomOrigin, FourLaneDrumPad.RedDrum, 2, NoteFlags.Tremolo));
        difficulty.Notes.Add(Note(tom2Origin, FourLaneDrumPad.BlueDrum, 20, NoteFlags.Tremolo));

        var published = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);
        var records = EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger);
        var authoredFinalPad = new EliteDrumFinalPadIdentity(Instrument.ProDrums,
            (int) FourLaneDrumPad.RedDrum);

        using (Assert.EnterMultipleScope())
        {
            // Stage 2: the authored phrase resolves (all members land on RedDrum) and
            // publishes alongside both unrelated resolved components, deterministically
            // ordered by their authored tick span.
            Assert.That(published, Has.Count.EqualTo(3));
            Assert.That(published.Select(descriptor => descriptor.AuthoredStartTick),
                Is.EqualTo(new uint[] { 0, 0, 20 }));
            Assert.That(published.All(descriptor => descriptor.IsFinalIdentityResolved), Is.True);

            // The authored members are claimed by the phrase record: they appear in
            // exactly one published descriptor, so no origin is covered twice.
            Assert.That(published.Count(descriptor => descriptor.Origins.Contains(originA)),
                Is.EqualTo(1));
            Assert.That(published[0].Origins, Has.No.Member(originA));
            Assert.That(published[0].FinalPad, Is.EqualTo(redTarget));
            Assert.That(published[0].SourceIds, Is.EqualTo(new[] { "unrelated-tom" }));
            Assert.That(published[1].GameplayId,
                Is.EqualTo("ProDrums:authored-lane:EliteDrums_SnareLane:0:10"));
            Assert.That(published[1].FinalPad, Is.EqualTo(authoredFinalPad));
            Assert.That(published[1].SourceIds, Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(published[2].FinalPad, Is.EqualTo(blueTarget));

            // The phrase provenance survives separately, resolved to the same identity.
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].SourceIds, Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(records[0].FinalPad, Is.EqualTo(authoredFinalPad));
            Assert.That(records[0].IsFinalIdentityResolved, Is.True);
        }
    }

    [Test]
    public void Builder_NativeProvenanceFreeDataIsUnaffected()
    {
        // Native (non-conversion) notes carry no provenance and no ledger data: no
        // descriptors and no authored phrase records can be produced for them.
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(new DrumNote((int)FourLaneDrumPad.RedDrum, DrumNoteType.Neutral,
            DrumNoteFlags.None, NoteFlags.Tremolo, 1.0, 0));
        var emptyLedger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EliteDrumVisualDescriptorV1Builder.Build(difficulty, emptyLedger), Is.Empty);
            Assert.That(EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, emptyLedger),
                Is.Empty);
        }

        // Authored phrase data without surviving provenance-bearing notes (native-style)
        // stays invalid: no records, no publication.
        var sourceA = new EliteDrumSourceDefinition("a", 0, (int)EliteDrumPad.Snare, 0, 10);
        var sourceB = new EliteDrumSourceDefinition("b", 1, (int)EliteDrumPad.Snare, 5, 10);
        var sourceC = new EliteDrumSourceDefinition("c", 2, (int)EliteDrumPad.Snare, 8, 10);
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(), new[]
            {
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_SnareLane, 0, 10,
                    new[] { sourceA, sourceB, sourceC }),
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger), Is.Empty);
            Assert.That(EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger),
                Is.Empty);
        }
    }

    [Test]
    public void AuthoredBuilder_SplitsMixedPhraseWhenEveryFinalGroupIsAValidLane()
    {
        // Six surviving members split into two final pads; each group independently
        // reaches the surviving-member minimum, so phrase membership supports separate
        // final groups and each publishes its own descriptor.
        var (difficulty, ledger) = MixedFinalPadFixture(
            (FourLaneDrumPad.RedDrum, 3), (FourLaneDrumPad.YellowDrum, 3));

        var records = EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger);
        var published = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(records, Has.Count.EqualTo(2));
            Assert.That(records.All(record => record.IsFinalIdentityResolved), Is.True);

            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(published.Select(descriptor => descriptor.FinalPad.Pad),
                Is.EqualTo(new[] { (int) FourLaneDrumPad.RedDrum, (int) FourLaneDrumPad.YellowDrum }));
            Assert.That(published.Select(descriptor => descriptor.GameplayId), Is.EqualTo(new[]
            {
                "ProDrums:authored-lane:EliteDrums_SnareLane:0:100:pad:1",
                "ProDrums:authored-lane:EliteDrums_SnareLane:0:100:pad:2",
            }));
            Assert.That(published.All(descriptor => descriptor.Origins.Count == 3), Is.True);
            // The split groups partition the phrase membership without overlap.
            Assert.That(published.SelectMany(descriptor => descriptor.Origins).Distinct().Count(),
                Is.EqualTo(6));
        }
    }

    [Test]
    public void AuthoredBuilder_MixedPhraseWithSubMinimumFinalGroupIsMalformedInsteadOfArbitrarilySplit()
    {
        // Three survivors land on RedDrum and one on BlueCymbal: the tiny BlueCymbal
        // group cannot stand as its own lane, so the phrase is malformed as a whole.
        // The majority RedDrum pad is never chosen arbitrarily and no partial split
        // publishes only the large group.
        var (difficulty, ledger) = MixedFinalPadFixture(
            (FourLaneDrumPad.RedDrum, 3), (FourLaneDrumPad.BlueCymbal, 1));

        var records = EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger), Is.Empty);
            // The malformed record is retained as explicitly unresolved provenance with
            // its full membership for diagnostics.
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].FinalPad.IsUnresolved, Is.True);
            Assert.That(records[0].IsFinalIdentityResolved, Is.False);
            Assert.That(records[0].Origins, Has.Count.EqualTo(4));
            Assert.That(records[0].SourceIds, Has.Count.EqualTo(4));
        }
    }

    [Test]
    public void AuthoredBuilder_PublishesResolvedKickPadIdentityInsteadOfTreatingPadZeroAsUnresolved()
    {
        // Defensive pad-value blindness check: hand members whose final children carry
        // pad 0 (the Kick pad value) must resolve and publish, because resolution is
        // gated on the explicit state, never on the pad value.
        var sourceA = new EliteDrumSourceDefinition("a", 0, (int)EliteDrumPad.Snare, 0, 10);
        var sourceB = new EliteDrumSourceDefinition("b", 1, (int)EliteDrumPad.Snare, 5, 10);
        var sourceC = new EliteDrumSourceDefinition("c", 2, (int)EliteDrumPad.Snare, 8, 10);
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(), new[]
            {
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_SnareLane, 0, 10,
                    new[] { sourceA, sourceB, sourceC }),
            });
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceA), FourLaneDrumPad.Kick, 0));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceB), FourLaneDrumPad.Kick, 5));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceC), FourLaneDrumPad.Kick, 8));

        var records = EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger);
        var published = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);
        var resolvedKick = new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int) FourLaneDrumPad.Kick);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].FinalPad, Is.EqualTo(resolvedKick));
            Assert.That(records[0].FinalPad.IsResolved, Is.True);
            Assert.That(records[0].IsFinalIdentityResolved, Is.True);
            // Pad 0 is a valid resolved pad: the record is published, not dropped as
            // "unresolved because the pad is zero".
            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].FinalPad, Is.EqualTo(resolvedKick));
            Assert.That(published[0].VisualIdentity.AppearanceColorSlot, Is.EqualTo(0));
            Assert.That(published[0].VisualIdentity.AppearanceModelKey,
                Is.EqualTo(nameof(FourLaneDrumPad.Kick)));
        }
    }

    [Test]
    public void AuthoredBuilder_NeverLeaksAuthoredOrSourcePadIntoPublishedIdentity()
    {
        // An authored RideLane whose gems land on the Pro BlueCymbal final pad: the
        // published final identity and appearance name BlueCymbal only. Source provenance
        // (authored Ride pads) stays on the origins, never in final-pad fields.
        var authoredPad = EliteDrumPad.Ride;
        var sourceA = new EliteDrumSourceDefinition("a", 0, (int) authoredPad, 0, 10, 1.0, 0.25);
        var sourceB = new EliteDrumSourceDefinition("b", 1, (int) authoredPad, 5, 10, 2.0, 0.25);
        var sourceC = new EliteDrumSourceDefinition("c", 2, (int) authoredPad, 8, 10, 3.0, 0.25);
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(), new[]
            {
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_RideLane, 0, 10,
                    new[] { sourceA, sourceB, sourceC }),
            });
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceA), FourLaneDrumPad.BlueCymbal, 0));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceB), FourLaneDrumPad.BlueCymbal, 5));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceC), FourLaneDrumPad.BlueCymbal, 8));

        var records = EliteDrumVisualDescriptorV1Builder.BuildAuthoredLanePhraseRecords(difficulty, ledger);
        var published = EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger);
        var expectedFinalPad = new EliteDrumFinalPadIdentity(Instrument.ProDrums,
            (int) FourLaneDrumPad.BlueCymbal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].FinalPad, Is.EqualTo(expectedFinalPad));
            // Source provenance keeps the authored Ride pad; the final identity does not.
            Assert.That(records[0].Origins.All(origin =>
                origin.Source.Pad == (int) EliteDrumPad.Ride), Is.True);
            Assert.That(records[0].FinalPad.Pad, Is.Not.EqualTo((int) EliteDrumPad.Ride));

            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].FinalPad, Is.EqualTo(expectedFinalPad));
            Assert.That(published[0].VisualIdentity.AppearanceModelKey,
                Is.EqualTo(nameof(FourLaneDrumPad.BlueCymbal)));
            Assert.That(published[0].VisualIdentity.AppearanceColorSlot,
                Is.EqualTo((int) FourLaneDrumPad.BlueCymbal));
            Assert.That(published[0].VisualIdentity.AppearanceModelKey,
                Is.Not.EqualTo(nameof(EliteDrumPad.Ride)));
        }
    }

    [Test]
    public void Builder_SkipsComponentWithMixedFinalPadsInsteadOfChoosingArbitrarily()
    {
        // A ledger component whose surviving final children land on two different final
        // pads is malformed for publication: nothing is emitted and no pad wins.
        var sourceA = new EliteDrumSourceDefinition("red-gem", 0, (int)EliteDrumPad.Snare, 0, 10, 1.0, 0.25);
        var sourceB = new EliteDrumSourceDefinition("blue-gem", 1, (int)EliteDrumPad.Tom2, 5, 10, 2.0, 0.25);
        var originA = new EliteDrumConversionOrigin(sourceA);
        var originB = new EliteDrumConversionOrigin(sourceB);
        var target = new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int) FourLaneDrumPad.RedDrum);
        var mixedLedger = new EliteDrumConversionLedger(
            EliteDrumFinalPadComponentBuilder.Build(new[]
            {
                new EliteDrumSourceMembership(originA, target, 0, 10),
                new EliteDrumSourceMembership(originB, target, 0, 10),
            }), Array.Empty<EliteDrumDroppedOrigin>());
        var mixedDifficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        mixedDifficulty.Notes.Add(Note(originA, FourLaneDrumPad.RedDrum, 0, NoteFlags.Tremolo));
        mixedDifficulty.Notes.Add(Note(originB, FourLaneDrumPad.BlueDrum, 5, NoteFlags.Tremolo));
        Assert.That(EliteDrumVisualDescriptorV1Builder.Build(mixedDifficulty, mixedLedger), Is.Empty);

        // Control: children agreeing on one final pad publish with that true identity
        // even when the ledger's grouping target differs from the children.
        var agreedDifficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        agreedDifficulty.Notes.Add(Note(originA, FourLaneDrumPad.RedDrum, 0, NoteFlags.Tremolo));
        agreedDifficulty.Notes.Add(Note(originB, FourLaneDrumPad.RedDrum, 5, NoteFlags.Tremolo));
        var published = EliteDrumVisualDescriptorV1Builder.Build(agreedDifficulty, mixedLedger);
        Assert.That(published, Has.Count.EqualTo(1));
        Assert.That(published[0].FinalPad, Is.EqualTo(target));
        Assert.That(published[0].SourceIds, Is.EqualTo(new[] { "red-gem", "blue-gem" }));
    }

    private static (InstrumentDifficulty<DrumNote> Difficulty, EliteDrumConversionLedger Ledger)
        AuthoredLaneFixture(PhraseType phraseType)
    {
        var authoredPad = EliteDrumAuthoredLanePhraseTypes.ToAuthoredPad(phraseType);
        var sourceA = new EliteDrumSourceDefinition("a", 0, (int) authoredPad, 0, 10, 1.0, 0.25);
        var sourceB = new EliteDrumSourceDefinition("b", 1, (int) authoredPad, 5, 10, 2.0, 0.25);
        var sourceC = new EliteDrumSourceDefinition("c", 2, (int) authoredPad, 8, 10, 3.0, 0.25);
        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(),
            new[] { new EliteDrumAuthoredLanePhrase(phraseType, 0, 10, new[] { sourceA, sourceB, sourceC }) });
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceA), FourLaneDrumPad.YellowDrum, 0));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceB), FourLaneDrumPad.YellowDrum, 5));
        difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(sourceC), FourLaneDrumPad.YellowDrum, 8));
        return (difficulty, ledger);
    }

    /// <summary>
    /// Authored hand-lane fixture whose members are split across the given final pads,
    /// with <paramref name="groups"/> entries of (final child pad, member count).
    /// </summary>
    private static (InstrumentDifficulty<DrumNote> Difficulty, EliteDrumConversionLedger Ledger)
        MixedFinalPadFixture(params (FourLaneDrumPad Pad, int Count)[] groups)
    {
        var members = new List<(EliteDrumSourceDefinition Source, FourLaneDrumPad Pad)>();
        var ordinal = 0;
        foreach (var (pad, count) in groups)
        {
            for (var i = 0; i < count; i++)
            {
                var source = new EliteDrumSourceDefinition($"{pad}:{ordinal}", ordinal,
                    (int) EliteDrumPad.Snare, (uint) (ordinal * 5), 10);
                members.Add((source, pad));
                ordinal++;
            }
        }

        var ledger = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
            Array.Empty<EliteDrumDroppedOrigin>(),
            new[]
            {
                new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_SnareLane, 0, 100,
                    members.Select(member => member.Source)),
            });
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        foreach (var (source, pad) in members)
        {
            difficulty.Notes.Add(Note(new EliteDrumConversionOrigin(source), pad, source.StartTick));
        }

        return (difficulty, ledger);
    }

    private static EliteDrumVisualDescriptorV1 ResolvedPadDerivedDescriptor()
    {
        var source = new EliteDrumSourceDefinition("tom", 0, (int)EliteDrumPad.Tom1, 0, 10, 1.0, 0.25);
        var origin = new EliteDrumConversionOrigin(source);
        var target = new EliteDrumFinalPadIdentity(Instrument.ProDrums, (int)FourLaneDrumPad.RedDrum);
        var ledger = new EliteDrumConversionLedger(
            EliteDrumFinalPadComponentBuilder.Build(new[]
            {
                new EliteDrumSourceMembership(origin, target, 0, 10),
            }), Array.Empty<EliteDrumDroppedOrigin>());
        var difficulty = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, Difficulty.Expert);
        difficulty.Notes.Add(Note(origin, FourLaneDrumPad.RedDrum, 0, NoteFlags.Tremolo));

        return EliteDrumVisualDescriptorV1Builder.Build(difficulty, ledger).Single();
    }

    private static EliteDrumConversionOrigin Origin(string id, EliteDrumPad pad, uint tick)
        => new(new EliteDrumSourceDefinition(id, (int)tick, (int)pad, tick, 10));

    private static DrumNote Note(EliteDrumConversionOrigin origin, FourLaneDrumPad pad, uint tick,
        NoteFlags flags = NoteFlags.None)
        => new(pad, DrumNoteType.Neutral, DrumNoteFlags.None, flags, tick / 100.0, tick,
            conversionOrigin: origin);
}
