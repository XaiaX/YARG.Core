using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.IO;
using YARG.Core.Utility;

namespace YARG.Core.UnitTests.Game;

public sealed class EliteNoHiHatTests
{
    private static EliteDrumNote Note(EliteDrumNote.EliteDrumPad pad, double time,
        EliteDrumNote.EliteDrumsHatPedalType pedal = EliteDrumNote.EliteDrumsHatPedalType.Stomp)
    {
        return new EliteDrumNote(pad, DrumNoteType.Neutral, EliteDrumNote.EliteDrumsHatState.Closed,
            pedal, false, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumNote.EliteDrumsChannelFlag.None, time, (uint) (time * 480), false);
    }

    private static InstrumentDifficulty<EliteDrumNote> Difficulty(params EliteDrumNote[] notes) =>
        new(Instrument.EliteDrums, YARG.Core.Difficulty.Expert, new(notes), new(), new());

    private static YargProfile Profile() => new()
    {
        GameMode = GameMode.EliteDrums,
        CurrentInstrument = Instrument.EliteDrums,
    };

    [Test]
    public void AvailabilityAndDefaultsAreNativeEliteOnly()
    {
        Assert.That((ulong) Modifier.NoHiHat, Is.EqualTo(1UL << 15));
        Assert.That((GameMode.EliteDrums.PossibleModifiers(Instrument.EliteDrums).possible & Modifier.NoHiHat), Is.EqualTo(Modifier.NoHiHat));
        Assert.That((GameMode.FourLaneDrums.PossibleModifiers(Instrument.FourLaneDrums).possible & Modifier.NoHiHat), Is.EqualTo(Modifier.None));
        var (fallbackPossible, fallbackExcusable) = GameMode.EliteDrums.PossibleModifiers(Instrument.ProDrums);
        Assert.That(fallbackPossible & Modifier.NoHiHat, Is.EqualTo(Modifier.None));
        Assert.That(fallbackExcusable & Modifier.NoHiHat, Is.EqualTo(Modifier.NoHiHat));
        var profile = Profile();
        Assert.That(profile.IsNativeEliteDrums, Is.True);
        Assert.That(profile.AutoHiHatPedal, Is.True);
        Assert.That(profile.NoHiHatPedal, Is.False);
        Assert.That(profile.EffectiveAutoHiHatPedal, Is.True);
        profile.NoHiHatPedal = true;
        Assert.That(profile.EffectiveAutoHiHatPedal, Is.False);
        profile.CurrentInstrument = Instrument.ProDrums;
        Assert.That(profile.IsNativeEliteDrums, Is.False);
        Assert.That(profile.EffectiveAutoHiHatPedal, Is.False);
    }

    [Test]
    public void HatAndPedalSettingsAreIndependentAndPreserveTerminatorAndLinks()
    {
        var hat = Note(EliteDrumNote.EliteDrumPad.HiHat, 1);
        var snare = Note(EliteDrumNote.EliteDrumPad.Snare, 1);
        hat.AddChildNote(snare);
        var pedal = Note(EliteDrumNote.EliteDrumPad.HatPedal, 2);
        var terminator = Note(EliteDrumNote.EliteDrumPad.HatPedal, 3,
            EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator);
        var original = Difficulty(hat, pedal, terminator);
        var profile = Profile();
        profile.AddSingleModifier(Modifier.NoHiHat);
        var gameplay = original.Clone();
        profile.ApplyModifiers(gameplay, new SyncTrack(480));
        Assert.That(gameplay.Notes, Has.Count.EqualTo(3));
        Assert.That(gameplay.Notes[0].Pad, Is.EqualTo((int) EliteDrumNote.EliteDrumPad.Snare));
        Assert.That(gameplay.Notes[1].Pad, Is.EqualTo((int) EliteDrumNote.EliteDrumPad.HatPedal));
        Assert.That(original.Notes[0].Pad, Is.EqualTo((int) EliteDrumNote.EliteDrumPad.HiHat));

        profile.NoHiHatPedal = true;
        gameplay = original.Clone();
        profile.ApplyModifiers(gameplay, new SyncTrack(480));
        Assert.That(gameplay.Notes, Has.Count.EqualTo(2));
        Assert.That(gameplay.Notes[0].Pad, Is.EqualTo((int) EliteDrumNote.EliteDrumPad.Snare));
        Assert.That(gameplay.Notes[1].IsInvisibleTerminator, Is.True);
        Assert.That(gameplay.Notes[0].PreviousNote, Is.Null);
        Assert.That(gameplay.Notes[0].NextNote, Is.SameAs(gameplay.Notes[1]));
        Assert.That(gameplay.Notes[1].PreviousNote, Is.SameAs(gameplay.Notes[0]));
        Assert.That(gameplay.Notes[1].NextNote, Is.Null);
    }

    [Test]
    public void PedalOnlyRemovesPedalsAndRetainsHatChild()
    {
        var pedal = Note(EliteDrumNote.EliteDrumPad.HatPedal, 1);
        pedal.AddChildNote(Note(EliteDrumNote.EliteDrumPad.HiHat, 1));
        var difficulty = Difficulty(pedal);
        var profile = Profile();
        profile.NoHiHatPedal = true;
        profile.ApplyModifiers(difficulty, new SyncTrack(480));
        Assert.That(difficulty.Notes, Has.Count.EqualTo(1));
        Assert.That(difficulty.Notes[0].Pad, Is.EqualTo((int) EliteDrumNote.EliteDrumPad.HiHat));
        Assert.That(difficulty.Notes[0].HatState, Is.EqualTo(EliteDrumNote.EliteDrumsHatState.Indifferent));
    }

    [Test]
    public void ParentActivationAndTerminatorPreservation()
    {
        var pedal = Note(EliteDrumNote.EliteDrumPad.HatPedal, 1);
        pedal.ActivateFlag(NoteFlags.CodaEnd | NoteFlags.StarPowerEnd);
        pedal.ActivateFlag(DrumNoteFlags.StarPowerActivator);
        var hat = Note(EliteDrumNote.EliteDrumPad.HiHat, 1);
        pedal.AddChildNote(hat);
        var difficulty = Difficulty(pedal);
        var profile = Profile();
        profile.NoHiHatPedal = true;
        profile.ApplyModifiers(difficulty, new SyncTrack(480));
        Assert.That(difficulty.Notes[0].IsCodaEnd, Is.True);
        Assert.That(difficulty.Notes[0].IsStarPowerEnd, Is.True);
        Assert.That(difficulty.Notes[0].IsStarPowerActivator, Is.True);
        Assert.That(difficulty.Notes[0].HatState, Is.EqualTo(EliteDrumNote.EliteDrumsHatState.Indifferent));
    }

    [Test]
    public void Version14ReplayDefaultsPedalSettings()
    {
        var profile = Profile();
        profile.AutoHiHatPedal = false;
        profile.NoHiHatPedal = true;
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
            profile.Serialize(writer);
        var bytes = ms.ToArray();
        bytes[0] = 14; // Previous replay version, before the two trailing pedal booleans.
        var buffer = FixedArray<byte>.Alloc(bytes.Length - 2);
        bytes.AsSpan(0, bytes.Length - 2).CopyTo(buffer.Span);
        var stream = new FixedArrayStream(buffer);
        var replay = new YargProfile(ref stream);
        Assert.That(replay.AutoHiHatPedal, Is.True);
        Assert.That(replay.NoHiHatPedal, Is.False);
    }

    [Test]
    public void ProfileSettingsRoundTripInJsonAndReplayBinary()
    {
        var profile = Profile();
        profile.AutoHiHatPedal = false;
        profile.NoHiHatPedal = true;
        profile.AddSingleModifier(Modifier.NoHiHat);
        var json = JsonConvert.DeserializeObject<YargProfile>(JsonConvert.SerializeObject(profile))!;
        Assert.That(json.AutoHiHatPedal, Is.False);
        Assert.That(json.NoHiHatPedal, Is.True);
        Assert.That(json.IsModifierActive(Modifier.NoHiHat), Is.True);

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
            profile.Serialize(writer);
        var bytes = ms.ToArray();
        var buffer = FixedArray<byte>.Alloc(bytes.Length);
        bytes.CopyTo(buffer.Span);
        var stream = new FixedArrayStream(buffer);
        var replay = new YargProfile(ref stream);
        Assert.That(replay.Version, Is.EqualTo(16));
        Assert.That(replay.AutoHiHatPedal, Is.False);
        Assert.That(replay.NoHiHatPedal, Is.True);
        Assert.That(replay.IsModifierActive(Modifier.NoHiHat), Is.True);
    }
}
