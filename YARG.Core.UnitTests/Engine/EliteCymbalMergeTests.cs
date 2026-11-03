// pattern: Imperative Shell
using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteCymbalMergeTests
{
    private static readonly float[] Thresholds = { 1, 2, 3, 4, 5, 6 };

    internal static EliteDrumsLayoutPreset Layout(string id)
    {
        var layout = EliteDrumsLayoutPreset.Full;
        var sources = id.Split('+');
        var target = layout.Slots.First(slot => slot.Cymbal == sources[1]);
        foreach (var slot in layout.Slots)
            if (sources.Contains(slot.Cymbal)) slot.Cymbal = null;
        target.Cymbal = id;
        foreach (var slot in layout.Slots)
            if (slot.Kind == EliteDrumsLayoutSlotKind.Ordinary && slot.Drum == null && slot.Cymbal == null)
                slot.Kind = EliteDrumsLayoutSlotKind.Empty;
        layout.Merges = new[] { new EliteDrumsLayoutMerge { EffectiveId = id, Sources = sources } };
        return layout;
    }

    internal static EliteDrumNote Note(EliteDrumPad pad, double time = 1, bool flam = false) =>
        new(pad, DrumNoteType.Accent, EliteDrumsHatState.Open, EliteDrumsHatPedalType.Stomp,
            flam, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.Blue, time, (uint)(time * 960), false);

    internal static InstrumentDifficulty<EliteDrumNote> Chart(params EliteDrumNote[] notes) =>
        new(Instrument.EliteDrums, Difficulty.Expert, new(notes), new(), new());

    private static void Project(InstrumentDifficulty<EliteDrumNote> notes, EliteDrumsLayoutPreset layout, byte policy = 1)
    {
        EliteDrumsCymbalProjection.Apply(notes, layout, policy);
    }

    private static int OriginalPad(EliteDrumNote note) => note.OriginalPad;
    private static int ColorPad(EliteDrumNote note, bool keep) => note.GetColorPad(keep);

    [TestCase("left-crash+ride", EliteDrumPad.LeftCrash, EliteDrumPad.Ride, EliteDrumPad.HiHat)]
    [TestCase("left-crash+right-crash", EliteDrumPad.LeftCrash, EliteDrumPad.RightCrash, EliteDrumPad.HiHat)]
    [TestCase("ride+right-crash", EliteDrumPad.Ride, EliteDrumPad.RightCrash, EliteDrumPad.LeftCrash)]
    public void SinglesShareKeeperButKeepAuthoredColorAndPairsExpand(string id, EliteDrumPad displaced,
        EliteDrumPad keeper, EliteDrumPad destination)
    {
        var single = Note(displaced);
        var chord = Note(displaced, 2, true);
        chord.AddChildNote(Note(keeper, 2));
        var source = Chart(single, chord);
        var copy = source.Clone();
        Project(copy, Layout(id));
        var gems = copy.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(gems.Select(n => n.Pad), Is.EqualTo(new[] { (int)keeper, (int)destination, (int)keeper }));
            Assert.That(gems.Select(OriginalPad), Is.EqualTo(new[] { (int)displaced, (int)displaced, (int)keeper }));
            Assert.That(ColorPad(gems[0], false), Is.EqualTo((int)displaced));
            Assert.That(ColorPad(gems[1], false), Is.EqualTo((int)destination));
            Assert.That(ColorPad(gems[1], true), Is.EqualTo((int)displaced));
            Assert.That(EliteDrumsColorRoles.GetRole(gems[1], true, true),
                Is.EqualTo(displaced == EliteDrumPad.LeftCrash ? EliteDrumsColorRole.LeftCrash : EliteDrumsColorRole.Ride));
            Assert.That(EliteDrumsColorRoles.GetRole(gems[1], true),
                Is.EqualTo(destination == EliteDrumPad.HiHat ? EliteDrumsColorRole.HatIndifferent : EliteDrumsColorRole.LeftCrash));
            Assert.That(gems[1].IsFlam, Is.True);
            Assert.That(gems[1].Dynamics, Is.EqualTo(DrumNoteType.Accent));
            Assert.That(gems[1].ChannelFlag, Is.EqualTo(EliteDrumsChannelFlag.Blue));
            Assert.That(source.Notes[0].Pad, Is.EqualTo((int)displaced));
            Assert.That(source.Notes[1].Pad, Is.EqualTo((int)displaced));
        });
        if (destination == EliteDrumPad.HiHat) Assert.That(gems[1].IsIndifferent, Is.True);
        Project(copy, Layout(id));
        Assert.That(copy.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Select(n => n.Pad), Is.EqualTo(gems.Select(n => n.Pad)), "Projection is idempotent.");
    }

    [Test]
    public void NearbyTicksDoNotExpandAndLegacyPolicyLeavesNotesUnchanged()
    {
        var notes = Chart(Note(EliteDrumPad.LeftCrash), Note(EliteDrumPad.Ride, 1.002));
        var legacy = notes.Clone();
        Project(legacy, Layout("left-crash+ride"), 0);
        Assert.That(legacy.Notes.Select(n => n.Pad), Is.EqualTo(new[] { 4, 8 }));
        Project(notes, Layout("left-crash+ride"));
        Assert.That(notes.Notes.Select(n => n.Pad), Is.EqualTo(new[] { 8, 8 }));
    }

    [Test]
    public void SharedInputCannotCompleteExpandedDestinationAndResetKeepsProjection()
    {
        var chord = Note(EliteDrumPad.LeftCrash);
        chord.AddChildNote(Note(EliteDrumPad.Ride));
        var notes = Chart(chord);
        var layout = Layout("left-crash+ride");
        Project(notes, layout);
        var engine = Engine(notes, layout);
        Press(engine, 1, EliteDrumsAction.EliteLeftCrash);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Press(engine, 1, EliteDrumsAction.EliteRide);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Press(engine, 1, EliteDrumsAction.EliteClosedHiHat);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        engine.Reset();
        Press(engine, 1, EliteDrumsAction.EliteOpenHiHat);
        Press(engine, 1, EliteDrumsAction.EliteRide);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
    }

    [Test]
    public void CollidingAuthoredDestinationRetainsEveryObligation()
    {
        var chord = Note(EliteDrumPad.LeftCrash);
        chord.AddChildNote(Note(EliteDrumPad.Ride));
        chord.AddChildNote(Note(EliteDrumPad.HiHat));
        var notes = Chart(chord);
        Project(notes, Layout("left-crash+ride"));
        Assert.That((1 + notes.Notes.Single().ChildNotes.Count), Is.EqualTo(3));
        Assert.That((1 + notes.Clone().Notes.Single().ChildNotes.Count), Is.EqualTo(3));
    }

    [Test]
    public void AliasesSharePendingFlamButRealNotesHavePriorityAndBotCompletesBothTargets()
    {
        var notes = Chart(Note(EliteDrumPad.LeftCrash, 1, true), Note(EliteDrumPad.Ride, 1.04, true));
        var layout = Layout("left-crash+ride");
        Project(notes, layout);
        var engine = Engine(notes, layout);
        int followUps = 0;
        engine.OnFlamFollowUp += (_, action, _, _) => { followUps++; Assert.That(action, Is.EqualTo(EliteDrumsAction.EliteRide)); };
        Press(engine, 1, EliteDrumsAction.EliteLeftCrash);
        Press(engine, 1.04, EliteDrumsAction.EliteRide);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(followUps, Is.Zero);
        Press(engine, 1.05, EliteDrumsAction.EliteLeftCrash);
        Assert.That(followUps, Is.EqualTo(1));
        Assert.That(engine.EngineStats.NoteScore, Is.EqualTo(150));
        var chord = Note(EliteDrumPad.LeftCrash);
        chord.AddChildNote(Note(EliteDrumPad.Ride));
        var botNotes = Chart(chord);
        Project(botNotes, layout);
        var bot = Engine(botNotes, layout, true);
        bot.Update(1);
        Assert.That(bot.EngineStats.NotesHit, Is.EqualTo(2));
    }

    [Test]
    public void AuthoredRollRequiresRealEntryOnNewTargetAndResumesCoveredKeeper()
    {
        EliteDrumNote Member(double time, int ordinal)
        {
            uint tick = (uint)(time * 960);
            var source = new EliteDrumSourceDefinition("roll", ordinal, (int)EliteDrumPad.LeftCrash, tick, 0, time);
            return new EliteDrumNote(EliteDrumPad.LeftCrash, DrumNoteType.Neutral, EliteDrumsHatState.Indifferent,
                EliteDrumsHatPedalType.Stomp, true, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.None,
                time, tick, false, source);
        }
        var first = Member(1, 0);
        var second = Member(1.04, 1);
        var expanded = Member(1.08, 2);
        expanded.AddChildNote(Note(EliteDrumPad.Ride, 1.08));
        var returning = Member(1.12, 3);
        var last = Member(1.16, 4);
        var notes = Chart(first, second, expanded, returning, last);
        notes.SetEliteDrumNativeAuthoredLaneRecords(new[] { new EliteDrumNativeAuthoredLaneRecord(0,
            PhraseType.EliteDrums_LeftCrashLane, 960, 1200,
            new[] { first, second, expanded, returning, last }.Select(n => n.SourceDefinition!)) });
        var layout = Layout("left-crash+ride");
        Project(notes, layout);
        var engine = Engine(notes, layout);
        Press(engine, 1, EliteDrumsAction.EliteLeftCrash);
        engine.Update(1.3);
        Assert.That(notes.Notes[1].WasHit, Is.True, "The unchanged physical roll continues.");
        Assert.That(notes.Notes[2].ChildNotes.Prepend(notes.Notes[2]).First(n => n.Pad == (int)EliteDrumPad.HiHat).WasMissed, Is.True,
            "Shared-pad cadence cannot complete a newly expanded destination.");
        Assert.That(notes.Notes[3].WasHit, Is.True, "Returning to an entered shared target resumes still-valid cadence.");
        Assert.That(notes.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Where(n => n.SourceDefinition != null).All(n => !n.IsFlam), Is.True);
        Assert.That(notes.Notes[2].SourceDefinition, Is.SameAs(expanded.SourceDefinition));
        engine.Reset();
        Press(engine, 1, EliteDrumsAction.EliteRide);
        Press(engine, 1.08, EliteDrumsAction.EliteClosedHiHat);
        Press(engine, 1.08, EliteDrumsAction.EliteRide);
        Press(engine, 1.12, EliteDrumsAction.EliteLeftCrash);
        engine.Update(1.4);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(6));
    }

    [TestCase("left-crash+ride", EliteDrumPad.LeftCrash, EliteDrumsAction.EliteLeftCrash, EliteDrumsAction.EliteRide)]
    [TestCase("left-crash+right-crash", EliteDrumPad.LeftCrash, EliteDrumsAction.EliteLeftCrash, EliteDrumsAction.EliteRightCrash)]
    [TestCase("ride+right-crash", EliteDrumPad.Ride, EliteDrumsAction.EliteRide, EliteDrumsAction.EliteRightCrash)]
    public void BothMemberBindingsOperateSharedTargetWithCanonicalFeedback(string id, EliteDrumPad displaced,
        EliteDrumsAction first, EliteDrumsAction keeper)
    {
        var notes = Chart(Note(displaced), Note(displaced, 2));
        var layout = Layout(id);
        var engine = Engine(notes, layout);
        int feedback = 0;
        engine.OnPadHit += (action, hit, _, _, _, _) =>
        {
            Assert.That(action, Is.EqualTo(keeper));
            Assert.That(hit, Is.True);
            feedback++;
        };
        Press(engine, 1, first);
        Press(engine, 2, keeper);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(2));
        Assert.That(feedback, Is.EqualTo(2));
    }

    [Test]
    public void MergedRollProtectsEitherMemberButOffPadStrikeRemainsAnOverhit()
    {
        EliteDrumNote Member(double time, int ordinal)
        {
            var note = Note(EliteDrumPad.LeftCrash, time);
            return new EliteDrumNote(note.Pad, note.Dynamics, note.HatState, note.HatPedalType, false,
                note.DrumFlags, note.Flags, note.ChannelFlag, note.Time, note.Tick, false,
                new EliteDrumSourceDefinition("protection", ordinal, note.Pad, note.Tick, 0, time));
        }
        var first = Member(1, 0);
        var last = Member(1.4, 1);
        var notes = Chart(first, last, Note(EliteDrumPad.Snare, 2));
        notes.SetEliteDrumNativeAuthoredLaneRecords(new[] { new EliteDrumNativeAuthoredLaneRecord(0,
            PhraseType.EliteDrums_LeftCrashLane, 960, 1500, new[] { first.SourceDefinition!, last.SourceDefinition! }) });
        var engine = Engine(notes, Layout("left-crash+ride"));
        int protectedHits = 0;
        engine.OnPadHit += (action, hit, _, inLane, _, _) =>
        {
            if (!hit && inLane) { protectedHits++; Assert.That(action, Is.EqualTo(EliteDrumsAction.EliteRide)); }
        };
        Press(engine, 1, EliteDrumsAction.EliteLeftCrash);
        Press(engine, 1.1, EliteDrumsAction.EliteRide);
        Press(engine, 1.2, EliteDrumsAction.EliteLeftCrash);
        Assert.That(protectedHits, Is.EqualTo(2));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(1));
        Assert.That(engine.EngineStats.Overhits, Is.Zero);
        Press(engine, 1.25, EliteDrumsAction.EliteClosedHiHat);
        Assert.That(engine.EngineStats.Overhits, Is.EqualTo(1));
    }

    [Test]
    public void EngineProjectionPreservesAlreadyPreparedActivationAndControlFlags()
    {
        var note = Note(EliteDrumPad.LeftCrash);
        note.ActivateFlag(DrumNoteFlags.StarPowerActivator);
        note.ActivateFlag(NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd |
            NoteFlags.Solo | NoteFlags.SoloStart | NoteFlags.SoloEnd | NoteFlags.CodaStart | NoteFlags.CodaEnd);
        var notes = Chart(note);
        var expected = note.Flags;
        _ = Engine(notes, Layout("left-crash+ride"));
        Assert.That(notes.Notes[0].IsStarPowerActivator, Is.True);
        Assert.That(notes.Notes[0].Flags, Is.EqualTo(expected));
    }

    [Test]
    public void ExpandedCodaChordKeepsBothTargetsAndNeverEarnsOrdinaryNoteScore()
    {
        var chord = Note(EliteDrumPad.LeftCrash);
        chord.AddChildNote(Note(EliteDrumPad.Ride));
        foreach (var note in chord.AllNotes) note.ActivateFlag(NoteFlags.BigRockEnding | NoteFlags.CodaEnd);
        var notes = Chart(chord);
        notes.Phrases.Add(new Phrase(PhraseType.BigRockEnding, 1, 1, 960, 960));
        var engine = Engine(notes, Layout("left-crash+ride"));
        Press(engine, 1, EliteDrumsAction.EliteLeftCrash);
        var projected = notes.Notes[0].ChildNotes.Prepend(notes.Notes[0]).ToArray();
        Assert.That(projected.Single(n => n.Pad == (int)EliteDrumPad.Ride).WasHit, Is.True);
        Assert.That(projected.Single(n => n.Pad == (int)EliteDrumPad.HiHat).WasHit, Is.False);
        Press(engine, 1, EliteDrumsAction.EliteRide);
        Assert.That(projected.Single(n => n.Pad == (int)EliteDrumPad.HiHat).WasHit, Is.False);
        Press(engine, 1, EliteDrumsAction.EliteSizzleHiHat);
        Assert.That(projected.All(n => n.WasHit), Is.True);
        Assert.That(engine.EngineStats.NoteScore, Is.Zero);
        Assert.That(engine.BaseNoteScore, Is.Zero);
    }

    [Test]
    public void DisplayPreferenceDefaultsOffAndSurvivesJsonAndSnapshot()
    {
        var profile = new YargProfile();
        Assert.That(profile.KeepOriginalCymbalColors, Is.False);
        profile.KeepOriginalCymbalColors = true;
        var restored = JsonConvert.DeserializeObject<YargProfile>(JsonConvert.SerializeObject(profile))!;
        Assert.That(restored.KeepOriginalCymbalColors, Is.True);
        Assert.That(profile.CreateReplaySnapshot().KeepOriginalCymbalColors, Is.True);
    }

    internal static EliteDrumsEngine Engine(InstrumentDifficulty<EliteDrumNote> chart, EliteDrumsLayoutPreset layout, bool bot = false,
        byte policy = EliteDrumsCymbalProjection.CURRENT_POLICY)
    {
        var template = EnginePreset.Default.Drums.Create(Thresholds, Thresholds, DrumsEngineParameters.DrumMode.Elite);
        var parameters = new DrumsEngineParameters(template.HitWindow, template.MaxMultiplier,
            template.StarMultiplierThresholds, template.SoloBonusStarMultiplierThresholds, template.Mode,
            template.NoStarPowerOverlap, template.EnableLanes, eliteCymbalMergePolicy: policy);
        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        return new EliteDrumsEngine(chart, sync, parameters, bot, true, cymbalLayout: layout);
    }

    internal static void Press(EliteDrumsEngine engine, double time, EliteDrumsAction action)
    {
        var input = GameInput.Create(time, action, .9f);
        engine.QueueInput(ref input);
        engine.Update(time);
    }
}
