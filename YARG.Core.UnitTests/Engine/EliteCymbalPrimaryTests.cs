// pattern: Functional Core
using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Input;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.UnitTests.Engine;

public sealed class EliteCymbalPrimaryTests
{
    [TestCase("left-crash", "right-crash", EliteDrumPad.LeftCrash, EliteDrumPad.RightCrash, EliteDrumPad.Ride)]
    [TestCase("left-crash", "ride", EliteDrumPad.LeftCrash, EliteDrumPad.Ride, EliteDrumPad.RightCrash)]
    [TestCase("ride", "left-crash", EliteDrumPad.Ride, EliteDrumPad.LeftCrash, EliteDrumPad.HiHat)]
    [TestCase("ride", "right-crash", EliteDrumPad.Ride, EliteDrumPad.RightCrash, EliteDrumPad.LeftCrash)]
    [TestCase("right-crash", "left-crash", EliteDrumPad.RightCrash, EliteDrumPad.LeftCrash, EliteDrumPad.HiHat)]
    [TestCase("right-crash", "ride", EliteDrumPad.RightCrash, EliteDrumPad.Ride, EliteDrumPad.LeftCrash)]
    public void OrderedChoiceKeepsPrimaryAndExpandsSecondaryWithBothColorModes(string primary, string secondary,
        EliteDrumPad primaryPad, EliteDrumPad secondaryPad, EliteDrumPad destination)
    {
        var layout = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 8, primary + ">" + secondary);
        var chord = EliteCymbalMergeTests.Note(secondaryPad, 2, true);
        chord.AddChildNote(EliteCymbalMergeTests.Note(primaryPad, 2));
        var chart = EliteCymbalMergeTests.Chart(EliteCymbalMergeTests.Note(secondaryPad), chord);
        EliteDrumsCymbalProjection.Apply(chart, layout);
        var gems = chart.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToArray();
        Assert.That(gems.Select(n => n.Pad), Is.EqualTo(new[] { (int)primaryPad, (int)destination, (int)primaryPad }));
        Assert.That(gems.Select(n => n.GetColorPad()), Is.EqualTo(new[] { (int)primaryPad, (int)destination, (int)primaryPad }));
        Assert.That(gems.Select(n => n.GetColorPad(true)), Is.EqualTo(new[] { (int)secondaryPad, (int)secondaryPad, (int)primaryPad }));
        Assert.That(gems[1].IsFlam, Is.True);
        Assert.That(gems[1].Dynamics, Is.EqualTo(DrumNoteType.Accent));
        if (destination == EliteDrumPad.HiHat) Assert.That(gems[1].IsIndifferent, Is.True);
        Assert.That(chart.Clone().Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Select(n => n.GetColorPad()),
            Is.EqualTo(gems.Select(n => n.GetColorPad())));
        var before = gems.Select(n => (n.Pad, n.OriginalPad, n.GetColorPad())).ToArray();
        EliteDrumsCymbalProjection.Apply(chart, layout);
        Assert.That(chart.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Select(n => (n.Pad, n.OriginalPad, n.GetColorPad())), Is.EqualTo(before));
    }

    [Test]
    public void CurrentPolicySinglesUsePrimaryColorWhileRecordedPolicyOneKeepsOriginal()
    {
        var layout = EliteCymbalMergeTests.Layout("left-crash+ride");
        var old = EliteCymbalMergeTests.Chart(EliteCymbalMergeTests.Note(EliteDrumPad.LeftCrash));
        var current = old.Clone();
        EliteDrumsCymbalProjection.Apply(old, layout, 1);
        EliteDrumsCymbalProjection.Apply(current, layout);
        Assert.That(old.Notes[0].GetColorPad(), Is.EqualTo((int)EliteDrumPad.LeftCrash));
        Assert.That(current.Notes[0].GetColorPad(), Is.EqualTo((int)EliteDrumPad.Ride));
        Assert.That(current.Notes[0].GetColorPad(true), Is.EqualTo((int)EliteDrumPad.LeftCrash));
    }

    [Test]
    public void ImportedOrientationPersistsAndNormalizesToLeftCrash()
    {
        var json = JObject.FromObject(EliteCymbalMergeTests.Layout("left-crash+ride"));
        json["SchemaVersion"] = 2;
        json["Merges"]![0]!["PrimarySource"] = "left-crash";
        var layout = json.ToObject<EliteDrumsLayoutPreset>()!;
        Assert.That(EliteDrumsLayoutValidator.Validate(layout).IsRuntimeSupported, Is.True);
        var roundTrip = JObject.Parse(JsonConvert.SerializeObject(layout.Copy()));
        Assert.That((string?)roundTrip["Merges"]![0]!["PrimarySource"], Is.EqualTo("left-crash"));
        Assert.That(EliteDrumsCymbalProjection.NormalizeAction(EliteDrumsAction.EliteRide, layout),
            Is.EqualTo(EliteDrumsAction.EliteLeftCrash));
    }
    [TestCase("left-crash+ride", "ride")]
    [TestCase("left-crash+right-crash", "right-crash")]
    [TestCase("ride+right-crash", "right-crash")]
    public void LegacySchemaMigratesToExistingKeeperOrientation(string id, string keeper)
    {
        var json = JObject.FromObject(EliteCymbalMergeTests.Layout(id));
        json["SchemaVersion"] = 1;
        ((JObject)json["Merges"]![0]!).Remove("PrimarySource");
        var restored = json.ToObject<EliteDrumsLayoutPreset>()!;
        Assert.That(restored.SchemaVersion, Is.EqualTo(2));
        Assert.That((string?)JObject.FromObject(restored)["Merges"]![0]!["PrimarySource"], Is.EqualTo(keeper));
        Assert.That(EliteDrumsLayoutValidator.Validate(restored).IsRuntimeSupported, Is.True);
    }

    [TestCase(2, "snare")]
    [TestCase(2, "right-crash")]
    [TestCase(2, "")]
    [TestCase(1, "left-crash")]
    public void InvalidOrAmbiguousPrimaryCannotEnterPlayback(int schema, string primary)
    {
        var json = JObject.FromObject(EliteCymbalMergeTests.Layout("left-crash+ride"));
        json["SchemaVersion"] = schema;
        json["Merges"]![0]!["PrimarySource"] = primary;
        var restored = json.ToObject<EliteDrumsLayoutPreset>()!;
        Assert.That(EliteDrumsLayoutValidator.Validate(restored).IsRuntimeSupported, Is.False);
        Assert.That(EliteDrumsLayoutResolver.TryPreviewResolve(restored, out _), Is.False);
    }

    public static string[] Choices => new[] { "left-crash>right-crash", "left-crash>ride", "ride>left-crash",
        "ride>right-crash", "right-crash>left-crash", "right-crash>ride" };

    private static EliteDrumPad Pad(string source) => source switch
    {
        "left-crash" => EliteDrumPad.LeftCrash,
        "ride" => EliteDrumPad.Ride,
        "right-crash" => EliteDrumPad.RightCrash,
        "hi-hat" => EliteDrumPad.HiHat,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private static EliteDrumsAction Action(string source) => source switch
    {
        "left-crash" => EliteDrumsAction.EliteLeftCrash,
        "ride" => EliteDrumsAction.EliteRide,
        "right-crash" => EliteDrumsAction.EliteRightCrash,
        "hi-hat" => EliteDrumsAction.EliteSizzleHiHat,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    [Test]
    public void RegistryHasExactlyTheSixOperatorOrderedChoices()
    {
        Assert.That(EliteDrumsCymbalPairs.All.Select(pair => pair.ChoiceId), Is.EqualTo(Choices));
    }

    [TestCaseSource(nameof(Choices))]
    public void ChoosingTheOtherOrientationRetainsColumnSourcesAndBothRows(string choice)
    {
        EliteDrumsCymbalPairs.TryGetChoice(choice, out var pair);
        var original = EliteDrumsLayoutPreset.Compact;
        int column = Array.FindIndex(original.Slots, slot => slot.Cymbal == pair!.PrimarySource);
        var layout = EliteDrumsLayoutEditor.Place(original, column, choice);
        layout = EliteDrumsLayoutEditor.Place(layout, column, pair!.SecondarySource + ">" + pair.PrimarySource);
        Assert.That(layout.Slots[column].Drum, Is.EqualTo(original.Slots[column].Drum));
        Assert.That(layout.Slots[column].Cymbal, Is.EqualTo(pair.EffectiveId));
        Assert.That(layout.Merges.Single().PrimarySource, Is.EqualTo(pair.SecondarySource));
        Assert.That(EliteDrumsLayoutEditor.GetUnassigned(layout), Is.Empty);
        Assert.That(EliteDrumsLayoutValidator.Validate(layout).IsRuntimeSupported, Is.True);
        var moved = EliteDrumsLayoutEditor.MoveColumn(layout, column, 8);
        var restored = JsonConvert.DeserializeObject<EliteDrumsLayoutPreset>(JsonConvert.SerializeObject(moved))!;
        Assert.That(restored.Merges.Single().PrimarySource, Is.EqualTo(pair.SecondarySource));
        Assert.That(EliteDrumsLayoutEditor.Place(restored, 9, pair.PrimarySource).Merges, Is.Empty);
    }

    [TestCaseSource(nameof(Choices))]
    public void BothAliasesHitPrimaryButExpandedDestinationRequiresItsOwnStrike(string choice)
    {
        EliteDrumsCymbalPairs.TryGetChoice(choice, out var pair);
        var layout = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 8, choice);
        var chord = EliteCymbalMergeTests.Note(Pad(pair!.SecondarySource), 3);
        chord.AddChildNote(EliteCymbalMergeTests.Note(Pad(pair.PrimarySource), 3));
        var chart = EliteCymbalMergeTests.Chart(EliteCymbalMergeTests.Note(Pad(pair.SecondarySource)),
            EliteCymbalMergeTests.Note(Pad(pair.PrimarySource), 2), chord);
        var engine = EliteCymbalMergeTests.Engine(chart, layout);
        int primaryFeedback = 0;
        engine.OnPadHit += (action, hit, _, _, _, _) =>
        {
            if (hit && action == Action(pair.PrimarySource)) primaryFeedback++;
        };
        EliteCymbalMergeTests.Press(engine, 1, Action(pair.SecondarySource));
        EliteCymbalMergeTests.Press(engine, 2, Action(pair.PrimarySource));
        EliteCymbalMergeTests.Press(engine, 3, Action(pair.SecondarySource));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
        EliteCymbalMergeTests.Press(engine, 3, Action(pair.PrimarySource));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(3));
        EliteCymbalMergeTests.Press(engine, 3, Action(pair.DestinationSource));
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(4));
        Assert.That(primaryFeedback, Is.EqualTo(3));
        engine.Reset();
        Assert.That(chart.Notes[0].GetColorPad(), Is.EqualTo((int)Pad(pair.PrimarySource)));
        var bot = EliteCymbalMergeTests.Engine(chart.Clone(), layout, true);
        bot.Update(4);
        Assert.That(bot.EngineStats.NotesHit, Is.EqualTo(4));
    }

    [TestCaseSource(nameof(Choices))]
    public void SecondaryAuthoredRollRequiresDestinationEntryAndResumesPrimaryCoverage(string choice)
    {
        EliteDrumsCymbalPairs.TryGetChoice(choice, out var pair);
        var secondary = Pad(pair!.SecondarySource);
        var members = Enumerable.Range(0, 5).Select(ordinal =>
        {
            double time = 1 + ordinal * .04;
            uint tick = (uint)Math.Round(time * 960);
            return new EliteDrumNote(secondary, DrumNoteType.Ghost, EliteDrumsHatState.Indifferent,
                EliteDrumsHatPedalType.Stomp, true, DrumNoteFlags.None, NoteFlags.None, EliteDrumsChannelFlag.Blue,
                time, tick, false, new EliteDrumSourceDefinition(choice, ordinal, (int)secondary, tick, 0, time));
        }).ToArray();
        var other = EliteCymbalMergeTests.Note(Pad(pair.PrimarySource), members[2].Time);
        other = new EliteDrumNote(other.Pad, other.Dynamics, other.HatState, other.HatPedalType,
            false, other.DrumFlags, other.Flags, other.ChannelFlag, other.Time, members[2].Tick, false);
        members[2].AddChildNote(other);
        var chart = EliteCymbalMergeTests.Chart(members);
        var phrase = secondary switch
        {
            EliteDrumPad.LeftCrash => PhraseType.EliteDrums_LeftCrashLane,
            EliteDrumPad.Ride => PhraseType.EliteDrums_RideLane,
            _ => PhraseType.EliteDrums_RightCrashLane
        };
        chart.SetEliteDrumNativeAuthoredLaneRecords(new[] { new EliteDrumNativeAuthoredLaneRecord(0,
            phrase, 960, 1200, members.Select(member => member.SourceDefinition!)) });
        var layout = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 8, choice);
        var engine = EliteCymbalMergeTests.Engine(chart, layout);
        EliteCymbalMergeTests.Press(engine, 1, Action(pair.SecondarySource));
        engine.Update(1.4);
        Assert.That(chart.Notes[1].WasHit, Is.True);
        Assert.That(chart.Notes[2].ChildNotes.Prepend(chart.Notes[2]).Single(note => note.OriginalPad == (int)secondary).WasMissed, Is.True);
        Assert.That(chart.Notes[3].WasHit, Is.True);
        Assert.That(chart.Notes.SelectMany(note => note.ChildNotes.Prepend(note)).Where(note => note.SourceDefinition != null)
            .Select(note => note.SourceDefinition), Is.EqualTo(members.Select(member => member.SourceDefinition)));
        engine.Reset();
        EliteCymbalMergeTests.Press(engine, 1, Action(pair.PrimarySource));
        EliteCymbalMergeTests.Press(engine, members[2].Time, Action(pair.DestinationSource));
        EliteCymbalMergeTests.Press(engine, members[2].Time, Action(pair.PrimarySource));
        engine.Update(1.4);
        Assert.That(engine.EngineStats.NotesHit, Is.EqualTo(6));
    }

}
