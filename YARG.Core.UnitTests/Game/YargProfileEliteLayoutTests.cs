using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Core.Extensions;
using YARG.Core.Game;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.Game;

/// <summary>
/// Profile persistence for the Elite Drums layout selection (JSON GUID, default
/// Compact) and the replay-only resolved layout content snapshot (profile binary
/// version 17): round-trips, legacy-version defaults, and hardening of the
/// snapshot JSON read/write against malformed, oversized, duplicate-property,
/// deep-nesting, and non-UTF-8 payloads.
/// </summary>
[TestFixture]
public sealed class YargProfileEliteLayoutTests
{
    private const int SNAPSHOT_BYTE_BOUND = 16 * 1024;

    private static readonly Guid CUSTOM_LAYOUT_ID = new("a34d92b2-6a4e-4d66-9a53-1cb54cf24d10");

    // Compact's arrangement under a custom identity: every source assigned exactly
    // once, no pairs, so it validates as structurally complete and runtime supported.
    private static EliteDrumsLayoutPreset CustomLayout()
    {
        var preset = EliteDrumsLayoutPreset.Compact.Copy();
        preset.Id = CUSTOM_LAYOUT_ID;
        preset.Name = "My layout";
        return preset;
    }

    // Structurally valid and complete, but it declares a snare+tom1 merge — the
    // runtime cannot activate merged lanes, so it must be neither recorded nor
    // read back as active.
    private static EliteDrumsLayoutPreset PairSplitLayout()
    {
        var preset = new EliteDrumsLayoutPreset("Pair split") { Id = Guid.NewGuid() };
        preset.Merges = new[]
        {
            new EliteDrumsLayoutMerge { EffectiveId = "snare+tom1", Sources = new[] { "snare", "tom1" } }
        };
        preset.Slots[0] = EliteDrumsLayoutSlot.Ordinary("snare+tom1", null);
        preset.Slots[1] = EliteDrumsLayoutSlot.Exclusive(EliteDrumsLayoutSlotKind.HiHat);
        preset.Slots[2] = EliteDrumsLayoutSlot.Ordinary("tom2", "left-crash");
        preset.Slots[3] = EliteDrumsLayoutSlot.Ordinary("tom3", "ride");
        preset.Slots[4] = EliteDrumsLayoutSlot.Ordinary(null, "right-crash");
        return preset;
    }

    private static YargProfile SerializeAndRead(YargProfile original)
    {
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            using var writer = new BinaryWriter(ms, Encoding.UTF8, true);
            original.Serialize(writer);
            bytes = ms.ToArray();
        }

        using var buffer = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(buffer.Span);
        var stream = new FixedArrayStream(buffer);
        return new YargProfile(ref stream);
    }

    // ----------------------------------------------------------------
    // JSON persistence (profile settings and duplication)
    // ----------------------------------------------------------------

    [Test]
    public void NewProfile_DefaultsToCompactLayoutIdentity()
    {
        var profile = new YargProfile();
        Assert.Multiple(() =>
        {
            Assert.That(profile.EliteDrumsLayout, Is.EqualTo(EliteDrumsLayoutPreset.CompactId));
            Assert.That(profile.ReplayEliteDrumsLayout, Is.Null);
        });
    }

    // Profiles saved before the layout field existed have no value in their JSON;
    // they load with the Compact default rather than an empty GUID.
    [Test]
    public void LegacyJsonProfile_WithoutLayoutField_DefaultsToCompact()
    {
        var profile = JsonConvert.DeserializeObject<YargProfile>("{\"Name\":\"Legacy\"}")!;
        Assert.That(profile.EliteDrumsLayout, Is.EqualTo(EliteDrumsLayoutPreset.CompactId));
    }

    // Profile duplication is JSON-based (no Core Copy API), so the layout identity
    // must survive a serialize/deserialize round-trip untouched.
    [Test]
    public void LayoutIdentity_SurvivesJsonRoundTrip()
    {
        var original = new YargProfile { Name = "LayoutPicker" };
        original.EliteDrumsLayout = CUSTOM_LAYOUT_ID;

        var json = JsonConvert.SerializeObject(original);
        Assert.That(json, Does.Contain("EliteDrumsLayout"));

        var restored = JsonConvert.DeserializeObject<YargProfile>(json)!;
        Assert.That(restored.EliteDrumsLayout, Is.EqualTo(CUSTOM_LAYOUT_ID));
    }

    // The resolved layout content is replay-only state: it must never leak into
    // the JSON profile, unlike the layout identity GUID which is a saved preference.
    [Test]
    public void ReplayLayoutContent_IsNotJsonSerialized()
    {
        var original = new YargProfile { Name = "ReplayOnly" };
        original.ReplayEliteDrumsLayout = CustomLayout();

        var json = JsonConvert.SerializeObject(original);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("ReplayEliteDrumsLayout"));
            Assert.That(json, Does.Not.Contain("My layout"));
            Assert.That(json, Does.Contain("EliteDrumsLayout"));
        });

        var restored = JsonConvert.DeserializeObject<YargProfile>(json)!;
        Assert.That(restored.ReplayEliteDrumsLayout, Is.Null);
    }

    // ----------------------------------------------------------------
    // Binary replay persistence (profile version 17)
    // ----------------------------------------------------------------

    [Test]
    public void LayoutIdentityAndContent_SurviveBinaryRoundTrip()
    {
        var original = new YargProfile { Name = "ElitePlayer" };
        original.EliteDrumsLayout = CUSTOM_LAYOUT_ID;
        original.ReplayEliteDrumsLayout = CustomLayout();

        var restored = SerializeAndRead(original);
        var snapshot = restored.ReplayEliteDrumsLayout!;
        Assert.Multiple(() =>
        {
            Assert.That(restored.Version, Is.EqualTo(17));
            Assert.That(restored.EliteDrumsLayout, Is.EqualTo(CUSTOM_LAYOUT_ID));
            Assert.That(snapshot.Id, Is.EqualTo(CUSTOM_LAYOUT_ID));
            Assert.That(snapshot.Name, Is.EqualTo("My layout"));
            Assert.That(snapshot.Slots, Has.Length.EqualTo(EliteDrumsLayoutPreset.SLOT_COUNT));
            Assert.That(snapshot.Slots[2].Drum, Is.EqualTo("tom1"));
            Assert.That(snapshot, Is.Not.SameAs(original.ReplayEliteDrumsLayout));
        });
    }

    [Test]
    public void AbsentLayoutContent_RoundTripsAsNull_WithIdentityIntact()
    {
        var original = new YargProfile { Name = "NoSnapshot" };
        original.EliteDrumsLayout = CUSTOM_LAYOUT_ID;
        Assert.That(original.ReplayEliteDrumsLayout, Is.Null);

        var restored = SerializeAndRead(original);
        Assert.Multiple(() =>
        {
            Assert.That(restored.EliteDrumsLayout, Is.EqualTo(CUSTOM_LAYOUT_ID));
            Assert.That(restored.ReplayEliteDrumsLayout, Is.Null);
        });
    }

    // A unicode layout name must round-trip as long as its UTF-8 byte count is
    // within the bound — the read side measures bytes, not characters.
    [Test]
    public void UnicodeLayoutName_WithinByteBound_RoundTrips()
    {
        var original = new YargProfile { Name = "UnicodePlayer" };
        original.EliteDrumsLayout = CUSTOM_LAYOUT_ID;
        original.ReplayEliteDrumsLayout = CustomLayout();
        original.ReplayEliteDrumsLayout.Name = new string('レ', 4000); // 12000 UTF-8 bytes < bound
        Assert.That(Encoding.UTF8.GetByteCount(original.ReplayEliteDrumsLayout.Name),
            Is.InRange(1, SNAPSHOT_BYTE_BOUND));

        var restored = SerializeAndRead(original);
        Assert.Multiple(() =>
        {
            Assert.That(restored.ReplayEliteDrumsLayout, Is.Not.Null);
            Assert.That(restored.ReplayEliteDrumsLayout!.Name, Is.EqualTo(new string('レ', 4000)));
            Assert.That(restored.EliteDrumsLayout, Is.EqualTo(CUSTOM_LAYOUT_ID));
        });
    }

    // 6000 characters is under the old character-based bound but 18000 UTF-8 bytes
    // is over it: the write side must refuse by byte count, never produce a replay
    // this build would refuse to read back.
    [Test]
    public void UnicodeLayoutName_BeyondByteBound_IsRejectedAtWrite()
    {
        var original = new YargProfile { Name = "TooBig" };
        original.ReplayEliteDrumsLayout = CustomLayout();
        original.ReplayEliteDrumsLayout.Name = new string('レ', 6000);
        Assert.That(original.ReplayEliteDrumsLayout.Name.Length, Is.LessThan(SNAPSHOT_BYTE_BOUND));

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        Assert.Throws<InvalidDataException>(() => original.Serialize(writer));
    }

    // The snapshot must be a deep copy: mutating the source layout after capture
    // can never reach the recorded replay state.
    [Test]
    public void ReplaySnapshot_DeepCopiesLayoutContent()
    {
        var original = new YargProfile { Name = "DeepCopy" };
        original.ReplayEliteDrumsLayout = CustomLayout();

        var snapshot = original.CreateReplaySnapshot();
        var captured = snapshot.ReplayEliteDrumsLayout!;
        original.ReplayEliteDrumsLayout.Name = "edited";
        original.ReplayEliteDrumsLayout.Slots[0].Drum = "edited";

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ReplayEliteDrumsLayout, Is.Not.SameAs(original.ReplayEliteDrumsLayout));
            Assert.That(captured.Name, Is.EqualTo("My layout"));
            Assert.That(captured.Slots, Is.Not.SameAs(original.ReplayEliteDrumsLayout.Slots));
            Assert.That(captured.Slots[0].Drum, Is.EqualTo("snare"));
            Assert.That(captured.Slots[0], Is.Not.SameAs(original.ReplayEliteDrumsLayout.Slots[0]));
        });
    }

    // Two profiles recorded back-to-back keep independent snapshot copies, and the
    // v17 tail leaves the stream exactly aligned for the next reader.
    [Test]
    public void LayoutBlock_MultiProfileBoundariesStayAligned()
    {
        var first = new YargProfile { Name = "PlayerOne" };
        first.EliteDrumsLayout = CUSTOM_LAYOUT_ID;
        first.ReplayEliteDrumsLayout = CustomLayout();
        var second = new YargProfile { Name = "PlayerTwo" };
        second.ReplayEliteDrumsLayout = CustomLayout();

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            first.Serialize(writer);
            long boundary = ms.Position;
            second.Serialize(writer);
            Assert.That(ms.Length, Is.EqualTo(boundary * 2));
            writer.Write(0x12345678);
        }

        var bytes = ms.ToArray();
        using var buffer = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(buffer.Span);
        var stream = new FixedArrayStream(buffer);
        var readFirst = new YargProfile(ref stream);
        var readSecond = new YargProfile(ref stream);

        Assert.Multiple(() =>
        {
            Assert.That(readFirst.ReplayEliteDrumsLayout, Is.Not.SameAs(readSecond.ReplayEliteDrumsLayout));
            Assert.That(readFirst.EliteDrumsLayout, Is.EqualTo(CUSTOM_LAYOUT_ID));
            Assert.That(readSecond.EliteDrumsLayout, Is.EqualTo(EliteDrumsLayoutPreset.CompactId));
            Assert.That(stream.Read<int>(Endianness.Little), Is.EqualTo(0x12345678));
        });
    }

    // ----------------------------------------------------------------
    // Legacy versions
    // ----------------------------------------------------------------

    // v16 replays end after the resolved-drum block: no layout GUID, no snapshot.
    // They read as the built-in Compact layout, matching versions 9-13 (and older).
    [Test]
    public void Version16Profile_ReadsAsCompactLayoutWithoutSnapshot()
    {
        var original = new YargProfile { Name = "V16Replay" };
        original.GameMode = GameMode.EliteDrums;
        original.CurrentInstrument = Instrument.EliteDrums;

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            original.Serialize(writer);
        }

        // A v17 write with no snapshot appends exactly the 16-byte GUID plus the
        // one-byte presence flag; strip those and relabel the version as 16.
        var v16Bytes = ms.ToArray()[..^17];
        BitConverter.GetBytes(16).CopyTo(v16Bytes, 0);

        using var buffer = FixedArray<byte>.Alloc(v16Bytes.Length);
        v16Bytes.CopyTo(buffer.Span);
        var stream = new FixedArrayStream(buffer);
        var profile = new YargProfile(ref stream);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Version, Is.EqualTo(16));
            Assert.That(profile.Name, Is.EqualTo("V16Replay"));
            Assert.That(profile.CurrentInstrument, Is.EqualTo(Instrument.EliteDrums));
            Assert.That(profile.EliteDrumsLayout, Is.EqualTo(EliteDrumsLayoutPreset.CompactId));
            Assert.That(profile.ReplayEliteDrumsLayout, Is.Null);
            Assert.That(stream.Remaining, Is.Zero);
        });
    }

    // ----------------------------------------------------------------
    // Rejection: never record or activate an unsupported snapshot
    // ----------------------------------------------------------------

    [Test]
    public void UnsupportedLayoutContent_IsRejectedAtWrite()
    {
        var original = new YargProfile { Name = "BadWrite" };
        original.ReplayEliteDrumsLayout = PairSplitLayout();
        Assert.That(EliteDrumsLayoutValidator.Validate(original.ReplayEliteDrumsLayout).IsComplete, Is.True,
            "Fixture must be structurally complete; only runtime support may fail");

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        Assert.Throws<InvalidDataException>(() => original.Serialize(writer));
    }

    [Test]
    public void UnsupportedLayoutContent_IsRejectedAtRead_NotActivated()
    {
        Assert.That(ReadWithInjectedSnapshot(PairSplitLayout()), Is.Null,
            "Read must throw, never activate an unsupported layout");
    }

    [Test]
    public void MalformedSnapshotJson_IsRejectedAtRead()
    {
        Assert.That(ReadWithInjectedSnapshot("not-a-layout{{{"), Is.Null);
    }

    [TestCase(" trailing")]
    [TestCase(" {}")]
    public void SnapshotWithTrailingJsonContent_IsRejectedAtRead(string suffix)
    {
        string validLayoutJson = JsonConvert.SerializeObject(CustomLayout());
        Assert.That(ReadWithInjectedSnapshot(validLayoutJson + suffix), Is.Null,
            "A valid layout object must not hide trailing content in the length-prefixed snapshot.");
    }

    // A future or hand-edited schema (here: wrong slot count) must not activate.
    [Test]
    public void WrongSchemaSnapshot_IsRejectedAtRead()
    {
        Assert.That(ReadWithInjectedSnapshot("{\"SchemaVersion\":1,\"Slots\":[]}"), Is.Null);
    }

    // Last-value-wins on duplicate properties would let a hostile file smuggle a
    // second meaning past readers that only inspect the first occurrence.
    [Test]
    public void DuplicateJsonProperty_IsRejectedAtRead()
    {
        Assert.That(ReadWithInjectedSnapshot("{\"Name\":\"dup\",\"Name\":\"dup2\"}"), Is.Null);
    }

    // Deeply nested JSON is hostile payload, not a layout; the reader depth bound
    // must reject it before the validator ever runs.
    [Test]
    public void DeeplyNestedSnapshot_IsRejectedAtRead()
    {
        Assert.That(ReadWithInjectedSnapshot("{\"a\":" + new string('[', 32) + new string(']', 32) + "}"), Is.Null);
    }

    // Invalid UTF-8 byte sequences must be refused strictly, not lossily decoded
    // into replacement characters.
    [Test]
    public void InvalidUtf8Snapshot_IsRejectedAtRead()
    {
        Assert.That(ReadWithInjectedSnapshot(new byte[] { 0xFF, 0xFE, 0x81, 0x00, 0xC3 }), Is.Null);
    }

    [Test]
    public void TruncatedLengthPrefix_IsRejectedAsInvalidData()
    {
        var bytes = SerializeCurrentWithTrailingPresence((byte) 1); // presence flag, then nothing
        using var buffer = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(buffer.Span);
        var stream = new FixedArrayStream(buffer);
        Assert.Throws<InvalidDataException>(() => new YargProfile(ref stream));
    }

    [Test]
    public void InvalidPresenceFlag_IsRejectedAtRead()
    {
        var bytes = SerializeCurrentWithTrailingPresence((byte) 2);
        using var buffer = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(buffer.Span);
        var stream = new FixedArrayStream(buffer);
        Assert.Throws<InvalidDataException>(() => new YargProfile(ref stream));
    }

    // The snapshot length prefix is untrusted: a prefix naming more bytes than
    // remain (or more than the byte bound) is rejected before any decoding.
    [TestCase(SNAPSHOT_BYTE_BOUND + 1)] // over the bound, nothing follows
    [TestCase(SNAPSHOT_BYTE_BOUND)]     // within the bound, but more than the stream holds
    public void OversizedSnapshotLengthPrefix_IsRejectedBeforeDecoding(int claimedLength)
    {
        var bytes = SerializeCurrentWithTrailingPresence((byte) 1);
        using var ms = new MemoryStream();
        ms.Write(bytes);
        using var writer = new BinaryWriter(ms);
        WriteSevenBitEncodedInt(writer, claimedLength);
        writer.Write(new byte[8]); // a few trailing bytes, far fewer than claimed

        using var buffer = FixedArray<byte>.Alloc((int) ms.Length);
        ms.ToArray().CopyTo(buffer.Span);
        var stream = new FixedArrayStream(buffer);
        Assert.Throws<InvalidDataException>(() => new YargProfile(ref stream));
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static byte[] SerializeCurrentWithTrailingPresence(byte presence)
    {
        var profile = new YargProfile { Name = "Presence" };
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            profile.Serialize(writer);
        }

        var bytes = ms.ToArray();
        bytes[^1] = presence; // the v17 tail's presence flag is the final byte
        return bytes;
    }

    /// <summary>
    /// Serializes a snapshot-less profile, replaces its trailing "no snapshot" flag
    /// with the given payload as the recorded snapshot, and reads the profile back.
    /// Returns null when the read (correctly) throws InvalidDataException.
    /// </summary>
    private static YargProfile? ReadWithInjectedSnapshot(string json) =>
        ReadWithInjectedSnapshot(Encoding.UTF8.GetBytes(json));

    private static YargProfile? ReadWithInjectedSnapshot(EliteDrumsLayoutPreset layout) =>
        ReadWithInjectedSnapshot(JsonConvert.SerializeObject(layout));

    private static YargProfile? ReadWithInjectedSnapshot(byte[] jsonBytes)
    {
        var bytes = SerializeCurrentWithTrailingPresence((byte) 1);
        using var ms = new MemoryStream();
        ms.Write(bytes);
        using var writer = new BinaryWriter(ms);
        WriteSevenBitEncodedInt(writer, jsonBytes.Length);
        writer.Write(jsonBytes);

        using var buffer = FixedArray<byte>.Alloc((int) ms.Length);
        ms.ToArray().CopyTo(buffer.Span);
        var stream = new FixedArrayStream(buffer);
        try
        {
            return new YargProfile(ref stream);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void WriteSevenBitEncodedInt(BinaryWriter writer, int value)
    {
        uint v = (uint) value;
        while (v >= 0x80)
        {
            writer.Write((byte) ((v & 0x7F) | 0x80));
            v >>= 7;
        }
        writer.Write((byte) v);
    }
}
