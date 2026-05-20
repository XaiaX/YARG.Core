using NUnit.Framework;
using YARG.Core.Game;
using YARG.Core.Replays;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine.Guitar.Engines;
using YARG.Core.Engine;
using YARG.Core.Chart;
using System.IO;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.Replays
{
    [TestFixture]
    public class ReplayFrameFreeVocalsTests : global::YARG.Core.UnitTests.Engine.EngineTester
    {
        private static readonly VocalsEngineParameters EngineParameters = new(
            new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0),
            4,
            new float[] { 0.05f, 0.11f, 0.19f, 0.46f, 0.77f, 1.06f },
            new float[] { 0.05f, 0.1f, 0.2f, 0.35f, 0.65f, 0.95f },
            1.5f,
            0.5f,
            0.75,
            60.0,
            true,
            1000);

        /// <summary>
        /// Helper: round-trip a ReplayFrame through serialize/deserialize.
        /// Reduces boilerplate across all replay serialization tests.
        /// </summary>
        private static ReplayFrame RoundTrip(ReplayFrame original)
        {
            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);

            original.Serialize(writer);
            writer.Flush();

            memoryStream.Seek(0, SeekOrigin.Begin);
            var fixedArray = FixedArray<byte>.Alloc((int)memoryStream.Length);
            memoryStream.Read(fixedArray.Span);
            var stream = new FixedArrayStream(fixedArray);
            return new ReplayFrame(ref stream, 1);
        }

        /// <summary>
        /// Helper: create a ReplayFrame with the given profile and default empty stats/inputs.
        /// </summary>
        private static ReplayFrame CreateFrame(YargProfile profile)
        {
            var stats = new VocalsStats();
            return new ReplayFrame(profile, EngineParameters, stats, Array.Empty<YARG.Core.Input.GameInput>());
        }

        [Test]
        public void FreeVocalsFlag_SurvivesReplaySerializationRoundTrip()
        {
            // Arrange: Create a YargProfile with FreeVocals enabled
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                FreeHarmony = true
            };

            var originalFrame = CreateFrame(profile);

            // Act
            var deserializedFrame = RoundTrip(originalFrame);

            // Assert: Verify the FreeVocals flag is preserved
            Assert.Multiple(() =>
            {
                // Check that PROFILE_VERSION is correct (should be 9 from Phase 1 Task 1)
                Assert.AreEqual(9, deserializedFrame.Profile.Version, "Profile version should be 9");

                // Check that FreeHarmony flag is preserved
                Assert.IsTrue(deserializedFrame.Profile.FreeHarmony, "FreeHarmony flag should be true after round-trip");

                // Check that IsFreeVocals property is correct
                Assert.IsTrue(deserializedFrame.Profile.IsFreeVocals, "IsFreeVocals should be true for vocals with FreeHarmony");

                // Verify other important fields are preserved
                Assert.AreEqual(Instrument.Vocals, deserializedFrame.Profile.CurrentInstrument, "CurrentInstrument should be Vocals");
            });
        }

        [Test]
        public void FreeVocalsFlag_UsesCorrectEngineWhenDeserialized()
        {
            // Arrange: Create a YargProfile with FreeVocals enabled
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                FreeHarmony = true,
                Version = 9
            };

            var originalFrame = CreateFrame(profile);

            // Act: Serialize and deserialize the replay frame
            var deserializedFrame = RoundTrip(originalFrame);

            // Assert: Verify the deserialized profile identifies as free vocals
            Assert.IsTrue(deserializedFrame.Profile.IsFreeVocals,
                "Deserialized profile should be recognized as Free Vocals");

            // Assert: Verify that EngineManager.Register routes correctly when
            // given the deserialized profile's IsFreeVocals flag. This tests AC5.2 --
            // the replay playback path reads the deserialized profile and must use the
            // free-vocals Register overload so that the container gets FREE_HARMONY_INDEX.
            var manager = new EngineManager();
            var chart = GetChart();
            var notes = chart.FiveFretGuitar.GetDifficulty(Difficulty.Expert);
            var stubEngine = new YargFiveFretGuitarEngine(
                notes, chart.SyncTrack,
                EnginePreset.Default.FiveFretGuitar.Create(
                    StarMultiplierThresholds, SoloBonusStarMultiplierThresholds, false),
                isBot: false);

            var container = manager.Register(
                stubEngine,
                deserializedFrame.Profile.CurrentInstrument,
                freeVocals: deserializedFrame.Profile.IsFreeVocals,
                chart,
                RockMeterPreset.Normal);

            Assert.That(container.HarmonyIndex, Is.EqualTo(EngineManager.FREE_HARMONY_INDEX),
                "Free vocals replay frame must produce a container with FREE_HARMONY_INDEX");
            Assert.That(container.Instrument, Is.EqualTo(Instrument.Vocals),
                "Container instrument should match the deserialized profile");
        }

        [Test]
        public void NonFreeVocalsFlag_SurvivesReplaySerializationRoundTrip()
        {
            // Arrange: Create a YargProfile with FreeVocals disabled
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                FreeHarmony = false
            };

            var originalFrame = CreateFrame(profile);

            // Act
            var deserializedFrame = RoundTrip(originalFrame);

            // Assert: Verify the FreeVocals flag is false
            Assert.Multiple(() =>
            {
                // Check that PROFILE_VERSION is correct
                Assert.AreEqual(9, deserializedFrame.Profile.Version, "Profile version should be 9");

                // Check that FreeHarmony flag is preserved as false
                Assert.IsFalse(deserializedFrame.Profile.FreeHarmony, "FreeHarmony flag should be false after round-trip");

                // Check that IsFreeVocals property is false
                Assert.IsFalse(deserializedFrame.Profile.IsFreeVocals, "IsFreeVocals should be false for non-Free vocals");
            });
        }
    }
}
