using NUnit.Framework;
using YARG.Core.Game;
using YARG.Core.Replays;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine;
using YARG.Core.Chart;
using System.IO;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.Replays
{
    [TestFixture]
    public class ReplayFrameFreeVocalsTests
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

        [Test]
        public void FreeVocalsFlag_SurvivesReplaySerializationRoundTrip()
        {
            // Arrange: Create a YargProfile with FreeVocals enabled
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                FreeHarmony = true
            };

            // Create empty stats (they won't be used in this test)
            var stats = new VocalsStats();

            // Create ReplayFrame
            var originalFrame = new ReplayFrame(profile, EngineParameters, stats, Array.Empty<YARG.Core.Input.GameInput>());

            // Act: Serialize and deserialize
            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);

            originalFrame.Serialize(writer);
            writer.Flush();

            memoryStream.Seek(0, SeekOrigin.Begin);
            var fixedArray = FixedArray<byte>.Alloc((int)memoryStream.Length);
            memoryStream.Read(fixedArray.Span);
            var stream = new FixedArrayStream(fixedArray);
            var deserializedFrame = new ReplayFrame(ref stream, 1);

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
                Version = 9 // Ensure it's version 9
            };

            // Create empty stats (they won't be used in this test)
            var stats = new VocalsStats();

            // Create ReplayFrame
            var originalFrame = new ReplayFrame(profile, EngineParameters, stats, Array.Empty<YARG.Core.Input.GameInput>());

            // Act: Serialize and deserialize
            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);

            originalFrame.Serialize(writer);
            writer.Flush();

            memoryStream.Seek(0, SeekOrigin.Begin);
            var fixedArray = FixedArray<byte>.Alloc((int)memoryStream.Length);
            memoryStream.Read(fixedArray.Span);
            var stream = new FixedArrayStream(fixedArray);
            var deserializedFrame = new ReplayFrame(ref stream, 1);

            // Assert: Verify that the profile would choose the correct engine
            // This tests AC5.2 - that the deserialized profile would use YargFreeVocalsEngine
            Assert.IsTrue(deserializedFrame.Profile.IsFreeVocals,
                "Profile should be recognized as Free Vocals and choose YargFreeVocalsEngine during playback");
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

            // Create empty stats (they won't be used in this test)
            var stats = new VocalsStats();

            // Create ReplayFrame
            var originalFrame = new ReplayFrame(profile, EngineParameters, stats, Array.Empty<YARG.Core.Input.GameInput>());

            // Act: Serialize and deserialize
            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);

            originalFrame.Serialize(writer);
            writer.Flush();

            memoryStream.Seek(0, SeekOrigin.Begin);
            var fixedArray = FixedArray<byte>.Alloc((int)memoryStream.Length);
            memoryStream.Read(fixedArray.Span);
            var stream = new FixedArrayStream(fixedArray);
            var deserializedFrame = new ReplayFrame(ref stream, 1);

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