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

        private static ReplayFrame RoundTrip(ReplayFrame original, int version)
        {
            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);

            original.Serialize(writer);
            writer.Flush();

            var bytes = memoryStream.ToArray();
            var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
            bytes.CopyTo(fixedArray.Span);
            var stream = new FixedArrayStream(fixedArray);
            return new ReplayFrame(ref stream, version);
        }

        private static ReplayFrame RoundTripV16(ReplayFrame original)
        {
            return RoundTrip(original, 16);
        }

        private static ReplayFrame RoundTripV15(ReplayFrame original)
        {
            return RoundTrip(original, 15);
        }

        private static ReplayFrame CreatePartyVocalsFrame(GameInput[][] perMicInputs)
        {
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.Vocals,
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
                PerMicInputs = perMicInputs
            };
            return frame;
        }

        [Test]
        public void PartyVocalsFrame_PerMicInputs_RoundTrip()
        {
            var perMicInputs = new GameInput[][]
            {
                new[]
                {
                    new GameInput(0.0, (int)VocalsAction.Pitch, 60f),
                    new GameInput(0.5, (int)VocalsAction.Pitch, 62f),
                    new GameInput(1.0, (int)VocalsAction.Pitch, 64f),
                },
                new[]
                {
                    new GameInput(0.1, (int)VocalsAction.Pitch, 64f),
                    new GameInput(0.6, (int)VocalsAction.Pitch, 65f),
                    new GameInput(1.1, (int)VocalsAction.Pitch, 67f),
                },
                new[]
                {
                    new GameInput(0.2, (int)VocalsAction.Pitch, 67f),
                    new GameInput(0.7, (int)VocalsAction.Pitch, 69f),
                    new GameInput(1.2, (int)VocalsAction.Pitch, 71f),
                },
            };

            var original = CreatePartyVocalsFrame(perMicInputs);
            var deserialized = RoundTripV16(original);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.PerMicInputs, Is.Not.Null, "PerMicInputs should not be null");
                Assert.That(deserialized.PerMicInputs.Length, Is.EqualTo(3), "PerMicInputs should have 3 arrays");
                Assert.That(deserialized.PerMicInputs[0], Is.EqualTo(perMicInputs[0]), "Mic 0 inputs should match");
                Assert.That(deserialized.PerMicInputs[1], Is.EqualTo(perMicInputs[1]), "Mic 1 inputs should match");
                Assert.That(deserialized.PerMicInputs[2], Is.EqualTo(perMicInputs[2]), "Mic 2 inputs should match");
            });
        }

        [Test]
        public void PartyVocalsFrame_SingleMic_RoundTrip()
        {
            var perMicInputs = new GameInput[][]
            {
                new[]
                {
                    new GameInput(0.0, (int)VocalsAction.Pitch, 60f),
                    new GameInput(0.5, (int)VocalsAction.Pitch, 62f),
                    new GameInput(1.0, (int)VocalsAction.Pitch, 64f),
                },
            };

            var original = CreatePartyVocalsFrame(perMicInputs);
            var deserialized = RoundTripV16(original);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.PerMicInputs, Is.Not.Null, "PerMicInputs should not be null");
                Assert.That(deserialized.PerMicInputs.Length, Is.EqualTo(1), "PerMicInputs should have 1 array");
                Assert.That(deserialized.PerMicInputs[0], Is.EqualTo(perMicInputs[0]), "Mic 0 inputs should match");
            });
        }

        [Test]
        public void NonPartyVocalsFrame_PerMicInputsIsNull()
        {
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.Vocals,
                Version = 10
            };

            var stats = new VocalsStats();
            var frame = new ReplayFrame(profile, EngineParameters, stats, Array.Empty<GameInput>());
            Assert.That(frame.PerMicInputs, Is.Null);

            var deserialized = RoundTripV15(frame);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.PerMicInputs, Is.Null, "Non-Party Vocals should have null PerMicInputs");
            });
        }

        [Test]
        public void V14Replay_PerMicInputsIsNull()
        {
            // Create a frame with no mic data (simulates pre-v15 replay)
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.Vocals,
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
                Assert.That(deserialized.PerMicInputs, Is.Null, "v14 deserialization should default PerMicInputs to null");
            });
        }

        [Test]
        public void ReplayFrame_V15PartyVocals_DiscardsMicBlock_StaysAligned()
        {
            // Test that v15 reading consumes exactly the legacy mic block bytes
            // and leaves the stream positioned correctly for subsequent data.

            using var memoryStream = new MemoryStream();
            using var writer = new BinaryWriter(memoryStream);

            // Write a minimal replay frame header
            new FourCC('R', 'P', 'F', 'M').Serialize(writer);

            // Write minimal profile (not PartyVocals to test legacy path)
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.Vocals,
                Version = 10
            };
            profile.Serialize(writer);

            // Write minimal engine parameters
            EngineParameters.Serialize(writer);

            // Write minimal stats
            var stats = new VocalsStats();
            stats.Serialize(writer);

            // Write inputs
            writer.Write(1); // count
            writer.Write(0.0); writer.Write((int)VocalsAction.Pitch); writer.Write(60);

            // Write legacy mic block (v15 format): 2 mics with total 5 float values
            writer.Write(2); // mic count
            writer.Write(3); // mic 0 length
            writer.Write(60f); writer.Write(62f); writer.Write(64f); // mic 0 pitches
            writer.Write(2); // mic 1 length
            writer.Write(64f); writer.Write(65f); // mic 1 pitches

            // Write a marker after the legacy block
            writer.Write((byte)0xFF); // sentinel byte

            writer.Flush();

            var bytes = memoryStream.ToArray();
            var fixedArray = FixedArray<byte>.Alloc(bytes.Length);
            bytes.CopyTo(fixedArray.Span);
            var stream = new FixedArrayStream(fixedArray);

            // Deserialize at v15 (should consume legacy block and preserve sentinel)
            var deserialized = new ReplayFrame(ref stream, 15);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.PerMicInputs, Is.Null, "v15 should discard mic block and set PerMicInputs to null");

                // The next byte should be our sentinel (proving correct alignment)
                byte nextByte = stream.ReadByte();
                Assert.That(nextByte, Is.EqualTo(0xFF), "Stream should be positioned after legacy block");
            });
        }

        [Test]
        public void PlainVocalsFrame_RoundTrip()
        {
            // Test plain GameMode.Vocals solo-vocals replay round-trip
            // (AC30.3: Solo Vocals replay playback unchanged).
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.Vocals,
                Version = 10
            };

            var stats = new VocalsStats();
            var inputs = new GameInput[]
            {
                new(0.0, (int)VocalsAction.Pitch, 60),
                new(0.5, (int)VocalsAction.Pitch, 62),
                new(1.0, (int)VocalsAction.Pitch, 64),
            };

            var frame = new ReplayFrame(profile, EngineParameters, stats, inputs);
            var deserialized = RoundTripV16(frame);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.PerMicInputs, Is.Null, "Plain Vocals should have null PerMicInputs");
                Assert.That(deserialized.Inputs, Is.EqualTo(inputs), "Inputs should round-trip intact");
            });
        }

        [Test]
        public void PartyVocalsRoundTrip_DeterministicScore()
        {
            var perMicInputs = new GameInput[][]
            {
                new[]
                {
                    new GameInput(0.0, (int)VocalsAction.Pitch, 60f),
                    new GameInput(0.5, (int)VocalsAction.Pitch, 62f),
                },
                new[]
                {
                    new GameInput(0.1, (int)VocalsAction.Pitch, 64f),
                    new GameInput(0.6, (int)VocalsAction.Pitch, 65f),
                },
            };

            var frame1 = CreatePartyVocalsFrame(perMicInputs);
            var deserialized1 = RoundTripV16(frame1);
            var deserialized2 = RoundTripV16(frame1);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized1.PerMicInputs.Length, Is.EqualTo(deserialized2.PerMicInputs.Length), "PerMicInputs should have same length");
                Assert.That(deserialized1.PerMicInputs[0], Is.EqualTo(deserialized2.PerMicInputs[0]), "Mic 0 should be deterministic");
                Assert.That(deserialized1.PerMicInputs[1], Is.EqualTo(deserialized2.PerMicInputs[1]), "Mic 1 should be deterministic");
            });
        }
    }
}
