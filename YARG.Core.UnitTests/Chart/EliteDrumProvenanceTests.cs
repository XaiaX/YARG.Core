using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Chart
{
    public sealed class EliteDrumProvenanceTests
    {
        private static EliteDrumSourceDefinition Source(string id, int ordinal, uint start, uint length = 10)
            => new(id, ordinal, (int)EliteDrumPad.Snare, start, length);

        [Test]
        public void RepeatedSourceIdsRemainDistinctByPhysicalOrdinal()
        {
            var first = Source("repeat", 0, 0);
            var second = Source("repeat", 1, 0);

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first.SourceId, Is.EqualTo(second.SourceId));
            Assert.That(first.PhysicalOrdinal, Is.Not.EqualTo(second.PhysicalOrdinal));
        }

        [Test]
        public void MembershipUsesHalfOpenInterval()
        {
            var membership = new EliteDrumSourceMembership(
                new EliteDrumConversionOrigin(Source("a", 0, 10, 5)),
                new EliteDrumFinalPadIdentity(Instrument.ProDrums, 1), 10, 15);

            Assert.That(membership.Contains(10), Is.True);
            Assert.That(membership.Contains(14), Is.True);
            Assert.That(membership.Contains(15), Is.False);
        }

        [Test]
        public void FinalPadIdentityResolutionIsExplicitAndNeverInferredFromThePadValue()
        {
            using (Assert.EnterMultipleScope())
            {
                // Unresolved identities keep a sentinel pad value for diagnostics, but
                // resolution is an explicit state: the pad value is never a gate signal.
                Assert.That(EliteDrumFinalPadIdentity.UnresolvedPad, Is.EqualTo(0));
                Assert.That(EliteDrumFinalPadIdentity.Unresolved.IsUnresolved, Is.True);
                Assert.That(EliteDrumFinalPadIdentity.Unresolved.IsResolved, Is.False);
                Assert.That(EliteDrumFinalPadIdentity.UnresolvedFor(Instrument.ProDrums).IsUnresolved, Is.True);
                Assert.That(EliteDrumFinalPadIdentity.UnresolvedFor(Instrument.ProDrums).Pad,
                    Is.EqualTo(EliteDrumFinalPadIdentity.UnresolvedPad));

                // Pad 0 is the valid final Kick pad: a resolved Kick identity must not
                // look unresolved, and it must stay distinct from the unresolved
                // sentinel despite the identical pad value.
                var resolvedKick = new EliteDrumFinalPadIdentity(Instrument.ProDrums,
                    (int)FourLaneDrumPad.Kick);
                Assert.That(resolvedKick.Pad, Is.EqualTo(EliteDrumFinalPadIdentity.UnresolvedPad));
                Assert.That(resolvedKick.Resolution, Is.EqualTo(EliteDrumFinalPadResolution.Resolved));
                Assert.That(resolvedKick.IsResolved, Is.True);
                Assert.That(resolvedKick.IsUnresolved, Is.False);
                Assert.That(EliteDrumFinalPadIdentity.UnresolvedFor(Instrument.ProDrums),
                    Is.Not.EqualTo(resolvedKick));

                // The canonical unresolved identity is the struct default.
                Assert.That(default(EliteDrumFinalPadIdentity),
                    Is.EqualTo(EliteDrumFinalPadIdentity.Unresolved));
            }
        }

        [Test]
        public void ComponentsMergeStrictOverlapTransitivelyButNotTouchingIntervals()
        {
            var target = new EliteDrumFinalPadIdentity(Instrument.ProDrums, 1);
            var a = new EliteDrumConversionOrigin(Source("a", 0, 0, 10));
            var b = new EliteDrumConversionOrigin(Source("b", 1, 5, 10));
            var c = new EliteDrumConversionOrigin(Source("c", 2, 14, 10));
            var touching = new EliteDrumConversionOrigin(Source("touch", 3, 24, 2));

            var result = EliteDrumFinalPadComponentBuilder.Build(new[]
            {
                new EliteDrumSourceMembership(a, target, 0, 10),
                new EliteDrumSourceMembership(b, target, 5, 15),
                new EliteDrumSourceMembership(c, target, 14, 24),
                new EliteDrumSourceMembership(touching, target, 24, 26),
            });

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].StartTick, Is.EqualTo(0));
            Assert.That(result[0].EndTick, Is.EqualTo(24));
            Assert.That(result[0].Origins, Has.Count.EqualTo(3));
            Assert.That(result[1].StartTick, Is.EqualTo(24));
            Assert.That(result[1].EndTick, Is.EqualTo(26));
        }

        [Test]
        public void LedgerRetainsDroppedFlamOriginWithoutMutableCollections()
        {
            var source = Source("flam", 4, 20);
            var origin = new EliteDrumConversionOrigin(source, 1);
            var ledger = new EliteDrumConversionLedger(
                new[] { new EliteDrumFinalPadComponentTestHelper(Instrument.ProDrums, 1, 20, 30, origin).Component },
                new[] { new EliteDrumDroppedOrigin(origin, "truncated") });

            Assert.That(ledger.DroppedOrigins.Single().Origin, Is.EqualTo(origin));
            Assert.That(ledger.Components.Single().Origins.Single(), Is.EqualTo(origin));
        }

        [Test]
        public void EliteClonePreservesAuthoredStateAndImmutableSource()
        {
            var source = Source("clone", 9, 30);
            var note = new EliteDrumNote(EliteDrumPad.HiHat, DrumNoteType.Accent,
                EliteDrumsHatState.Closed, EliteDrumsHatPedalType.Splash, true,
                DrumNoteFlags.None, NoteFlags.CodaEnd, EliteDrumsChannelFlag.Blue,
                1.0, 30, false, source, isFlatFlam: true);
            var clone = note.Clone();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(clone.Dynamics, Is.EqualTo(note.Dynamics));
                Assert.That(clone.HatState, Is.EqualTo(note.HatState));
                Assert.That(clone.HatPedalType, Is.EqualTo(note.HatPedalType));
                Assert.That(clone.IsFlam, Is.EqualTo(note.IsFlam));
                Assert.That(clone.IsFlatFlam, Is.True);
                Assert.That(clone.ChannelFlag, Is.EqualTo(note.ChannelFlag));
                Assert.That(clone.SourceDefinition, Is.SameAs(source));
            }
        }

        [Test]
        public void AuthoredLanePhraseMembershipIsHalfOpenAndSourceKeyed()
        {
            var insideStart = Source("start", 0, 10);
            var insideMiddle = Source("middle", 1, 15);
            var afterEnd = Source("after", 2, 20);
            var phrase = new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_SnareLane, 10, 20,
                new[] { insideStart, insideMiddle });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(phrase.Contains(10), Is.True);
                Assert.That(phrase.Contains(19), Is.True);
                Assert.That(phrase.Contains(20), Is.False);

                Assert.That(phrase.ContainsOrigin(new EliteDrumConversionOrigin(insideStart)), Is.True);
                // Flam expansions of a member source match the same membership.
                Assert.That(phrase.ContainsOrigin(new EliteDrumConversionOrigin(insideStart, 1)), Is.True);
                Assert.That(phrase.ContainsOrigin(new EliteDrumConversionOrigin(afterEnd)), Is.False);
                Assert.That(phrase.ContainsOrigin(null), Is.False);
            }
        }

        [Test]
        public void AuthoredLanePhraseTypesClassifyV1HandLanesAndPads()
        {
            var handLanes = new[]
            {
                PhraseType.EliteDrums_SnareLane, PhraseType.EliteDrums_HiHatLane,
                PhraseType.EliteDrums_LeftCrashLane, PhraseType.EliteDrums_Tom1Lane,
                PhraseType.EliteDrums_Tom2Lane, PhraseType.EliteDrums_Tom3Lane,
                PhraseType.EliteDrums_RideLane, PhraseType.EliteDrums_RightCrashLane,
            };
            var excluded = new[]
            {
                PhraseType.EliteDrums_KickLane, PhraseType.EliteDrums_HatPedalLane,
                PhraseType.TremoloLane, PhraseType.TrillLane, PhraseType.KickLane,
            };

            using (Assert.EnterMultipleScope())
            {
                Assert.That(handLanes.Select(EliteDrumAuthoredLanePhraseTypes.IsAuthoredHandLane),
                    Is.All.True);
                Assert.That(excluded.Select(EliteDrumAuthoredLanePhraseTypes.IsAuthoredHandLane),
                    Is.All.False);

                Assert.That(EliteDrumAuthoredLanePhraseTypes.IsHandPad((int)EliteDrumPad.Snare), Is.True);
                Assert.That(EliteDrumAuthoredLanePhraseTypes.IsHandPad((int)EliteDrumPad.Tom1), Is.True);
                Assert.That(EliteDrumAuthoredLanePhraseTypes.IsHandPad((int)EliteDrumPad.Ride), Is.True);
                Assert.That(EliteDrumAuthoredLanePhraseTypes.IsHandPad((int)EliteDrumPad.Kick), Is.False);
                Assert.That(EliteDrumAuthoredLanePhraseTypes.IsHandPad((int)EliteDrumPad.HatPedal), Is.False);

                Assert.That(EliteDrumAuthoredLanePhraseTypes.ToAuthoredPad(PhraseType.EliteDrums_SnareLane),
                    Is.EqualTo(EliteDrumPad.Snare));
                Assert.That(EliteDrumAuthoredLanePhraseTypes.ToAuthoredPad(PhraseType.EliteDrums_RightCrashLane),
                    Is.EqualTo(EliteDrumPad.RightCrash));
            }
        }

        [Test]
        public void LedgerExposesAuthoredLanePhrasesWhenProvidedAndEmptyOtherwise()
        {
            var source = Source("member", 0, 0);
            var empty = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
                Array.Empty<EliteDrumDroppedOrigin>());
            var populated = new EliteDrumConversionLedger(Array.Empty<EliteDrumFinalPadComponent>(),
                Array.Empty<EliteDrumDroppedOrigin>(), new[]
                {
                    new EliteDrumAuthoredLanePhrase(PhraseType.EliteDrums_Tom2Lane, 0, 10, new[] { source }),
                });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(empty.AuthoredLanePhrases, Is.Empty);
                Assert.That(populated.AuthoredLanePhrases, Has.Count.EqualTo(1));
                Assert.That(populated.AuthoredLanePhrases[0].LaneType, Is.EqualTo(PhraseType.EliteDrums_Tom2Lane));
                Assert.That(populated.AuthoredLanePhrases[0].MemberSources.Single(), Is.EqualTo(source));
            }
        }

        private sealed class EliteDrumFinalPadComponentTestHelper
        {
            public EliteDrumFinalPadComponent Component { get; }
            public EliteDrumFinalPadComponentTestHelper(Instrument instrument, int pad, uint start, uint end,
                EliteDrumConversionOrigin origin)
            {
                Component = EliteDrumFinalPadComponentBuilder.Build(new[]
                {
                    new EliteDrumSourceMembership(origin, new EliteDrumFinalPadIdentity(instrument, pad), start, end)
                }).Single();
            }
        }
    }
}
