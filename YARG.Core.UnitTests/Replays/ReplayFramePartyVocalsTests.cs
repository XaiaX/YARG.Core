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
            new HitWindowSettings(0.1, 0.1, 1.0, false, 0, 1, 1, 0, 0),
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

        private static ReplayFrame RoundTripCurrent(ReplayFrame original)
        {
            // Round-trip at the current replay version. Older fixed version numbers
            // can no longer be produced by the current writer (e.g. BaseStats writes
            // StarPowerRevives unconditionally but only reads it at >= 17), so any
            // fixed old version desyncs the stream.
            return RoundTrip(original, ReplayIO.REPLAY_VERSIONS.CURRENT);
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
            var deserialized = RoundTripCurrent(original);

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
            var deserialized = RoundTripCurrent(original);

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

            var deserialized = RoundTripCurrent(frame);

            // Flat stream format - no mic-specific field to check
            Assert.That(deserialized.Inputs, Is.Empty, "Non-Party Vocals should have empty inputs");
        }

        [Test]
        public void NonPartyVocalsFrame_CurrentVersion_DeserializesInputs()
        {
            // A non-Party-Vocals (solo) frame stores inputs as a flat stream with no
            // trailing per-mic block. Round-trip it at the current replay version and
            // confirm the inputs deserialize. (This previously read back as v14; current
            // serialization always writes the lane proximity field added by the autohit
            // lane rework, so v14 — which predates it — is no longer a format that current
            // serialization can produce.)
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

            var deserialized = RoundTripCurrent(frame);

            Assert.That(deserialized.Inputs.Length, Is.GreaterThan(0), "inputs should deserialize");
        }

        // NOTE: the legacy v15 per-mic block (YOLO party-vocals line) is gone.
        // Versions 15-18 follow the upstream layout; see ReplayIO.REPLAY_VERSIONS.

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
            var deserialized = RoundTripCurrent(frame);

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
            var deserialized1 = RoundTripCurrent(frame1);
            var deserialized2 = RoundTripCurrent(frame1);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized1.Inputs.Length, Is.EqualTo(deserialized2.Inputs.Length), "Inputs should have same length");
                Assert.That(deserialized1.Inputs, Is.EqualTo(deserialized2.Inputs), "Inputs should be deterministic");
            });
        }
    }
}
