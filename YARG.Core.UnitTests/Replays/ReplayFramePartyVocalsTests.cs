using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Replays;
using YARG.Core.Engine.Vocals;
using YARG.Core.Engine;
using YARG.Core.Input;
using System.IO;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.Replays
{
    [TestFixture]
    public class ReplayFramePartyVocalsTests
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

        private static ReplayFrame RoundTripV15(ReplayFrame original)
        {
            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);

            original.Serialize(writer);
            writer.Flush();

            var bytes = memoryStream.ToArray();
            var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
            bytes.CopyTo(fixedArray.Span);
            var stream = new FixedArrayStream(fixedArray);
            return new ReplayFrame(ref stream, 15);
        }

        private static ReplayFrame CreatePartyVocalsFrame(int micCount, float[][] micPitches)
        {
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.Vocals,
                FreeHarmony = true,
                Version = 10
            };

            var stats = new VocalsStats();
            var inputs = new GameInput[]
            {
                new(0.0, (int)VocalsAction.Pitch, 60),
                new(0.5, (int)VocalsAction.Pitch, 62),
                new(1.0, (int)VocalsAction.Pitch, 64),
            };

            var frame = new ReplayFrame(profile, EngineParameters, stats, inputs)
            {
                MicCount = micCount,
                MicPitches = micPitches
            };
            return frame;
        }

        [Test]
        public void PartyVocalsFrame_MicCountAndPitches_RoundTrip()
        {
            var micPitches = new float[][]
            {
                new[] { 60f, 62f, 64f },
                new[] { 64f, 65f, 67f },
                new[] { 67f, 69f, 71f },
            };

            var original = CreatePartyVocalsFrame(3, micPitches);
            var deserialized = RoundTripV15(original);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.MicCount, Is.EqualTo(3), "MicCount should round-trip");
                Assert.That(deserialized.MicPitches, Is.Not.Null, "MicPitches should not be null");
                Assert.That(deserialized.MicPitches.Length, Is.EqualTo(3), "MicPitches should have 3 arrays");
                Assert.That(deserialized.MicPitches[0], Is.EqualTo(new[] { 60f, 62f, 64f }), "Mic 0 pitches should match");
                Assert.That(deserialized.MicPitches[1], Is.EqualTo(new[] { 64f, 65f, 67f }), "Mic 1 pitches should match");
                Assert.That(deserialized.MicPitches[2], Is.EqualTo(new[] { 67f, 69f, 71f }), "Mic 2 pitches should match");
            });
        }

        [Test]
        public void NonPartyVocalsFrame_MicCountZeroAndNullPitches()
        {
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.Vocals,
                FreeHarmony = false,
                Version = 10
            };

            var stats = new VocalsStats();
            var frame = new ReplayFrame(profile, EngineParameters, stats, Array.Empty<GameInput>());
            Assert.That(frame.MicCount, Is.EqualTo(0));
            Assert.That(frame.MicPitches, Is.Null);

            var deserialized = RoundTripV15(frame);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.MicCount, Is.EqualTo(0), "Non-Party Vocals should have MicCount=0");
                Assert.That(deserialized.MicPitches, Is.Null, "Non-Party Vocals should have null MicPitches");
            });
        }

        [Test]
        public void V14Replay_DefaultsToZeroMicCount()
        {
            // Create a frame with no mic data (simulates pre-v15 replay)
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.Vocals,
                FreeHarmony = true,
                Version = 9
            };
            var stats = new VocalsStats();
            var inputs = new GameInput[]
            {
                new(0.0, (int)VocalsAction.Pitch, 60),
                new(0.5, (int)VocalsAction.Pitch, 62),
            };
            var frame = new ReplayFrame(profile, EngineParameters, stats, inputs);

            // Manually serialize only the pre-v15 fields
            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);
            frame.Serialize(writer);
            writer.Flush();

            var bytes = memoryStream.ToArray();
            var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
            bytes.CopyTo(fixedArray.Span);
            var stream = new FixedArrayStream(fixedArray);
            var deserialized = new ReplayFrame(ref stream, 14);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.MicCount, Is.EqualTo(0), "v14 deserialization should default MicCount to 0");
                Assert.That(deserialized.MicPitches, Is.Null, "v14 deserialization should default MicPitches to null");
            });
        }

        [Test]
        public void PartyVocalsRoundTrip_DeterministicScore()
        {
            var micPitches = new float[][]
            {
                new[] { 60f, 62f },
                new[] { 64f, 65f },
            };

            var frame1 = CreatePartyVocalsFrame(2, micPitches);
            var deserialized1 = RoundTripV15(frame1);
            var deserialized2 = RoundTripV15(frame1);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized1.MicCount, Is.EqualTo(deserialized2.MicCount), "MicCount should be deterministic");
                Assert.That(deserialized1.MicPitches[0], Is.EqualTo(deserialized2.MicPitches[0]), "Mic 0 should be deterministic");
                Assert.That(deserialized1.MicPitches[1], Is.EqualTo(deserialized2.MicPitches[1]), "Mic 1 should be deterministic");
            });
        }
    }
}
