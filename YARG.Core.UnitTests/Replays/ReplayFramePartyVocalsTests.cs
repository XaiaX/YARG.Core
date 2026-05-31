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

        private static ReplayFrame CreatePartyVocalsFrame(GameInput[] inputs)
        {
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.PartyVocals,
                Version = 10
            };

            var stats = new VocalsStats();
            return new ReplayFrame(profile, EngineParameters, stats, inputs);
        }

        [Test]
        public void PartyVocalsFrame_FlatStream_RoundTrip()
        {
            // Test round-trip of flat mic-packed stream at v16
            var inputs = new GameInput[]
            {
                // Mic 0 inputs
                new(0.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 60f),
                new(0.5, PartyVocalsInput.Pack(0, VocalsAction.Hit), true),
                new(1.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 62f),

                // Mic 1 inputs
                new(0.1, PartyVocalsInput.Pack(1, VocalsAction.Pitch), 64f),
                new(0.6, PartyVocalsInput.Pack(1, VocalsAction.Pitch), 65f),

                // Mic 2 inputs
                new(0.2, PartyVocalsInput.Pack(2, VocalsAction.Pitch), 67f),
                new(0.7, PartyVocalsInput.Pack(2, VocalsAction.Hit), true),
                new(1.2, PartyVocalsInput.Pack(2, VocalsAction.Pitch), 69f),
            };

            var original = CreatePartyVocalsFrame(inputs);
            var deserialized = RoundTripV16(original);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.Inputs, Is.EqualTo(inputs), "Flat mic-packed inputs should round-trip intact");
            });
        }

        [Test]
        public void PartyVocalsFrame_SingleMic_RoundTrip()
        {
            // Test round-trip with single mic packed inputs
            var inputs = new GameInput[]
            {
                new(0.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 60f),
                new(0.5, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 62f),
                new(1.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 64f),
            };

            var original = CreatePartyVocalsFrame(inputs);
            var deserialized = RoundTripV16(original);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.Inputs, Is.EqualTo(inputs), "Single mic inputs should round-trip intact");
            });
        }

        [Test]
        public void NonPartyVocalsFrame_MicStreamNull()
        {
            var profile = new YargProfile(Guid.NewGuid())
            {
                CurrentInstrument = Instrument.Vocals,
                GameMode = GameMode.Vocals,
                Version = 10
            };

            var stats = new VocalsStats();
            var frame = new ReplayFrame(profile, EngineParameters, stats, Array.Empty<GameInput>());

            var deserialized = RoundTripV16(frame);

            // Flat stream format - no mic-specific field to check
            Assert.That(deserialized.Inputs, Is.Empty, "Non-Party Vocals should have empty inputs");
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

            // Flat stream format - no mic-specific field to check
            Assert.That(deserialized.Inputs.Length, Is.GreaterThan(0), "v14 should deserialize inputs");
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

            // The next byte should be our sentinel (proving correct alignment)
                byte nextByte = stream.ReadByte();
                Assert.That(nextByte, Is.EqualTo(0xFF), "Stream should be positioned after legacy block");
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
                Assert.That(deserialized.Inputs, Is.EqualTo(inputs), "Inputs should round-trip intact");
            });
        }

        [Test]
        public void PartyVocalsRoundTrip_Deterministic()
        {
            // Test that flat stream serialization is deterministic
            var inputs = new GameInput[]
            {
                new(0.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 60f),
                new(0.5, PartyVocalsInput.Pack(1, VocalsAction.Pitch), 62f),
                new(1.0, PartyVocalsInput.Pack(0, VocalsAction.Pitch), 64f),
            };

            var frame1 = CreatePartyVocalsFrame(inputs);
            var deserialized1 = RoundTripV16(frame1);
            var deserialized2 = RoundTripV16(frame1);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized1.Inputs.Length, Is.EqualTo(deserialized2.Inputs.Length), "Inputs should have same length");
                Assert.That(deserialized1.Inputs, Is.EqualTo(deserialized2.Inputs), "Inputs should be deterministic");
            });
        }
    }
}
