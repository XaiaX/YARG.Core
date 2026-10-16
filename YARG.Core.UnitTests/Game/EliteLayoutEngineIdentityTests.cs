using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using YARG.Core.Chart;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Drums.Engines;
using YARG.Core.Game;
using YARG.Core.Input;

namespace YARG.Core.UnitTests.Game;

/// <summary>
/// Verifies that Elite Drums layouts are pure presentation: resolving or editing a layout
/// never rewrites chart note identity, and the same input events drive a real
/// <see cref="EliteDrumsEngine"/> to identical scoring, note hit state, optional-pedal
/// state and engine event streams across the built-in Compact/Full layouts, an arbitrary
/// snare+ride pairing, and a ten-lane split-kick layout.
/// </summary>
public sealed class EliteLayoutEngineIdentityTests
{
    private sealed record LayoutRun(
        int LaneCount,
        EliteDrumsLayoutKickMode KickMode,
        IReadOnlyList<(string Note, string Lane)> Routing,
        IReadOnlyList<string> EngineEvents,
        string StatsSnapshot,
        IReadOnlyList<string> NoteStates,
        IReadOnlyList<string> NoteIdentity,
        int NotesHit,
        int TotalNotes,
        int CommittedScore,
        int Overhits,
        int OptionalPedalNotes,
        int AssistedPedalNotes);

    // ---- Chart fixtures (real notes carrying authored source provenance) ----

    private static EliteDrumNote SourceNote(EliteDrumNote.EliteDrumPad pad, string sourceId, double time,
        int ordinal, bool isDoubleKick = false, bool isFlam = false,
        DrumNoteType dynamics = DrumNoteType.Neutral,
        EliteDrumNote.EliteDrumsHatState hat = EliteDrumNote.EliteDrumsHatState.Indifferent,
        EliteDrumNote.EliteDrumsHatPedalType pedal = EliteDrumNote.EliteDrumsHatPedalType.Stomp)
    {
        uint tick = (uint) (time * 960);
        return new EliteDrumNote(pad, dynamics, hat, pedal, isFlam, DrumNoteFlags.None, NoteFlags.None,
            EliteDrumNote.EliteDrumsChannelFlag.None, time, tick, isDoubleKick,
            new EliteDrumSourceDefinition(sourceId, ordinal, (int) pad, tick, 0, time, 0));
    }

    private static void Chain(IReadOnlyList<EliteDrumNote> parents)
    {
        for (int i = 1; i < parents.Count; i++)
        {
            parents[i - 1].NextNote = parents[i];
            parents[i].PreviousNote = parents[i - 1];
        }
    }

    /// <summary>Covers ordinary, double-kick and flam kicks, dynamics, a chord, an optional
    /// hi-hat pedal with invisible terminator, and every pad routed by gameplay.</summary>
    private static (InstrumentDifficulty<EliteDrumNote> Chart, List<EliteDrumNote> Notes) BuildChart()
    {
        var kick = SourceNote(EliteDrumNote.EliteDrumPad.Kick, "kick", 1, 0);
        var doubleKick = SourceNote(EliteDrumNote.EliteDrumPad.Kick, "kick", 2, 1, isDoubleKick: true);
        var flamKick = SourceNote(EliteDrumNote.EliteDrumPad.Kick, "kick", 3, 2, isFlam: true);
        var accentSnare = SourceNote(EliteDrumNote.EliteDrumPad.Snare, "snare", 4, 3, dynamics: DrumNoteType.Accent);
        var closedHat = SourceNote(EliteDrumNote.EliteDrumPad.HiHat, "hi-hat", 5, 4,
            hat: EliteDrumNote.EliteDrumsHatState.Closed);
        var ride = SourceNote(EliteDrumNote.EliteDrumPad.Ride, "ride", 6, 5);
        var chordSnare = SourceNote(EliteDrumNote.EliteDrumPad.Snare, "snare", 7, 6);
        var chordKick = SourceNote(EliteDrumNote.EliteDrumPad.Kick, "kick", 7, 7);
        chordSnare.AddChildNote(chordKick);
        var pedal = SourceNote(EliteDrumNote.EliteDrumPad.HatPedal, "hi-hat", 8, 8);
        var terminator = new EliteDrumNote(EliteDrumNote.EliteDrumPad.HatPedal, DrumNoteType.Neutral,
            EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumNote.EliteDrumsHatPedalType.InvisibleTerminator,
            false, DrumNoteFlags.None, NoteFlags.None, EliteDrumNote.EliteDrumsChannelFlag.None, 8.5, 8160, false);
        var ghostTom = SourceNote(EliteDrumNote.EliteDrumPad.Tom1, "tom1", 9, 9, dynamics: DrumNoteType.Ghost);

        var parents = new List<EliteDrumNote> { kick, doubleKick, flamKick, accentSnare, closedHat, ride,
            chordSnare, pedal, terminator, ghostTom };
        Chain(parents);
        var flat = new List<EliteDrumNote> { kick, doubleKick, flamKick, accentSnare, closedHat, ride,
            chordSnare, chordKick, pedal, terminator, ghostTom };
        return (new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            parents, new(), new()), flat);
    }

    /// <summary>Ordinary kick, double kick and flam kick plus a snare, played only with the
    /// single physical kick action that the engine defines for all kick notes.</summary>
    private static (InstrumentDifficulty<EliteDrumNote> Chart, List<EliteDrumNote> Notes) BuildKickChart()
    {
        var kick = SourceNote(EliteDrumNote.EliteDrumPad.Kick, "kick", 1, 0);
        var doubleKick = SourceNote(EliteDrumNote.EliteDrumPad.Kick, "kick", 2, 1, isDoubleKick: true);
        var flamKick = SourceNote(EliteDrumNote.EliteDrumPad.Kick, "kick", 3, 2, isFlam: true);
        var snare = SourceNote(EliteDrumNote.EliteDrumPad.Snare, "snare", 4, 3);
        var parents = new List<EliteDrumNote> { kick, doubleKick, flamKick, snare };
        Chain(parents);
        return (new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Expert,
            parents, new(), new()), parents);
    }

    private static readonly (double Time, EliteDrumsAction Action, float Velocity)[] PlayInputs =
    {
        (1, EliteDrumsAction.Kick, 0.8f),
        (2, EliteDrumsAction.Kick, 0.8f),
        (3, EliteDrumsAction.Kick, 0.8f),
        (4, EliteDrumsAction.EliteSnare, 1f),
        (5, EliteDrumsAction.EliteClosedHiHat, 0.8f),
        (5.5, EliteDrumsAction.EliteSplash, 0.8f), // unmatched pedal strike between notes
        (6, EliteDrumsAction.EliteRide, 0.8f),
        (7, EliteDrumsAction.EliteSnare, 0.8f),
        (7, EliteDrumsAction.Kick, 0.8f),
        (9, EliteDrumsAction.EliteTom1, 0.1f),
        (10, EliteDrumsAction.Kick, 0.8f), // freestyle strike after the final note
    };

    private static readonly (double Time, EliteDrumsAction Action, float Velocity)[] KickInputs =
    {
        (1, EliteDrumsAction.Kick, 0.8f),
        (2, EliteDrumsAction.Kick, 0.8f),
        (3, EliteDrumsAction.Kick, 0.8f),
        (4, EliteDrumsAction.EliteSnare, 0.8f),
    };

    // ---- Layouts under test ----

    private static List<(string Name, EliteDrumsLayoutPreset Preset)> Layouts()
    {
        // Arbitrary pairing: the ride cymbal joins the snare drum's lane.
        var snareRide = EliteDrumsLayoutEditor.Place(EliteDrumsLayoutPreset.Full, 0, "ride");
        // Ten lanes: all eight sources plus split Kick1/Kick2 columns.
        var splitKicks = EliteDrumsLayoutEditor.PlaceKick(
            EliteDrumsLayoutPreset.Full, 8, EliteDrumsLayoutSlotKind.Kick);
        splitKicks = EliteDrumsLayoutEditor.PlaceKick(splitKicks, 9, EliteDrumsLayoutSlotKind.Kick2);
        return
        [
            ("Compact", EliteDrumsLayoutPreset.Compact),
            ("Full", EliteDrumsLayoutPreset.Full),
            ("SnareRide", snareRide),
            ("SplitKicks", splitKicks),
        ];
    }

    /// <summary>Mirrors EliteDrumsPlayer: the pad decides the source ID, and split kick
    /// layouts route double kicks to the second pedal lane.</summary>
    private static string PresentationSource(EliteDrumNote note, EliteDrumsResolvedLayout layout)
    {
        if (note.Pad == (int) EliteDrumNote.EliteDrumPad.Kick && layout.KickMode == EliteDrumsLayoutKickMode.Split)
            return note.IsDoubleKick ? "kick2" : "kick1";
        return (EliteDrumNote.EliteDrumPad) note.Pad switch
        {
            EliteDrumNote.EliteDrumPad.Kick => "kick",
            EliteDrumNote.EliteDrumPad.Snare => "snare",
            EliteDrumNote.EliteDrumPad.HiHat or EliteDrumNote.EliteDrumPad.HatPedal => "hi-hat",
            EliteDrumNote.EliteDrumPad.LeftCrash => "left-crash",
            EliteDrumNote.EliteDrumPad.Tom1 => "tom1",
            EliteDrumNote.EliteDrumPad.Tom2 => "tom2",
            EliteDrumNote.EliteDrumPad.Tom3 => "tom3",
            EliteDrumNote.EliteDrumPad.Ride => "ride",
            EliteDrumNote.EliteDrumPad.RightCrash => "right-crash",
            _ => throw new ArgumentOutOfRangeException(nameof(note), note.Pad, null),
        };
    }

    private static string IdentityOf(EliteDrumNote note) =>
        $"{note.SourceDefinition?.SourceId ?? "-"}#{note.SourceDefinition?.PhysicalOrdinal ?? -1}" +
        $" pad={note.Pad} tick={note.Tick} doubleKick={note.IsDoubleKick} flam={note.IsFlam}";

    // ---- Engine runner ----

    private static LayoutRun Run(
        (InstrumentDifficulty<EliteDrumNote> Chart, List<EliteDrumNote> Notes) fixture,
        IReadOnlyList<(double Time, EliteDrumsAction Action, float Velocity)> inputs,
        EliteDrumsLayoutPreset preset, bool autoHiHatPedal)
    {
        var (chart, notes) = fixture;
        var resolved = EliteDrumsLayoutResolver.Resolve(preset);
        var routing = new List<(string Note, string Lane)>();
        foreach (var note in notes.Where(note => !note.IsInvisibleTerminator))
        {
            string source = PresentationSource(note, resolved);
            string lane = resolved.SourceToLane.TryGetValue(source, out int index) ? $"lane{index}" : "bars";
            routing.Add((IdentityOf(note), $"{source}@{lane}"));
        }

        var sync = new SyncTrack(480);
        sync.Tempos.Add(new TempoChange(120, 0, 0));
        var parameters = EnginePreset.Default.Drums.Create([1f, 2f, 3f, 4f, 5f, 6f],
            [1f, 2f, 3f, 4f, 5f, 6f], DrumsEngineParameters.DrumMode.ProFourLane);
        var engine = new EliteDrumsEngine(chart, sync, parameters, false, true, autoHiHatPedal);

        var events = new List<string>();
        engine.OnPadHit += (action, noteWasHit, bonus, overhitInLane, type, velocity) =>
            events.Add($"pad {action} hit={noteWasHit} bonus={bonus} overhitLane={overhitInLane}" +
                $" type={type} velocity={velocity:0.###}");
        engine.OnNoteHit += (_, note) => events.Add($"noteHit {IdentityOf(note)}");
        engine.OnNoteMissed += (_, note) => events.Add($"noteMissed {IdentityOf(note)}");
        engine.OnOverhit += () => events.Add("overhit");
        engine.OnPedalAssisted += note => events.Add($"pedalAssisted {IdentityOf(note)}");

        foreach (var group in inputs.GroupBy(input => input.Time))
        {
            foreach (var (time, action, velocity) in group)
            {
                var input = GameInput.Create(time, action, velocity);
                engine.QueueInput(ref input);
            }
            engine.Update(group.Key);
        }
        engine.Update(inputs[^1].Time + 0.5);

        var stats = engine.EngineStats;
        string snapshot = string.Join(", ",
            $"score={stats.CommittedScore}", $"noteScore={stats.NoteScore}", $"baseScore={engine.BaseScore}",
            $"hit={stats.NotesHit}", $"total={stats.TotalNotes}", $"chords={stats.TotalChords}",
            $"missed={stats.NotesMissed}", $"maxCombo={stats.MaxCombo}", $"overhits={stats.Overhits}",
            $"dynamics={stats.DynamicsBonus}", $"accents={stats.AccentsHit}/{stats.TotalAccents}",
            $"ghosts={stats.GhostsHit}/{stats.TotalGhosts}",
            $"optionalPedal={stats.OptionalPedalNotes}", $"pedalHits={stats.OptionalPedalHits}",
            $"assistedPedal={stats.AssistedPedalNotes}", $"percent={stats.Percent:0.####}");
        var states = notes.Select(note => $"{IdentityOf(note)} hit={note.WasHit} missed={note.WasMissed}" +
            $" velocity={(note.HitVelocity?.ToString("0.###") ?? "-")}").ToList();
        return new LayoutRun(resolved.Lanes.Count, resolved.KickMode, routing, events, snapshot, states,
            notes.Select(IdentityOf).ToList(), stats.NotesHit, stats.TotalNotes, stats.CommittedScore,
            stats.Overhits, stats.OptionalPedalNotes, stats.AssistedPedalNotes);
    }

    private static string LaneToken(LayoutRun run, string identityPrefix) =>
        run.Routing.Single(route => route.Note.StartsWith(identityPrefix, StringComparison.Ordinal))
            .Lane.Split('@')[1];

    // ---- Tests ----

    [Test]
    public void EliteLayoutEngine_IdentityInvariant()
    {
        var runs = Layouts().ToDictionary(
            layout => layout.Name, layout => Run(BuildChart(), PlayInputs, layout.Preset, true));

        // The four layouts must genuinely differ in presentation, or the invariance
        // comparison below would be vacuous.
        Assert.That(runs["Compact"].LaneCount, Is.EqualTo(5));
        Assert.That(runs["Full"].LaneCount, Is.EqualTo(8));
        Assert.That(runs["SnareRide"].LaneCount, Is.EqualTo(7));
        Assert.That(runs["SplitKicks"].LaneCount, Is.EqualTo(10));
        Assert.That(runs["SplitKicks"].KickMode, Is.EqualTo(EliteDrumsLayoutKickMode.Split));
        // Only the arbitrary pairing shares a snare/ride lane.
        Assert.That(LaneToken(runs["SnareRide"], "snare#3"), Is.EqualTo(LaneToken(runs["SnareRide"], "ride#5")));
        Assert.That(LaneToken(runs["Full"], "snare#3"), Is.Not.EqualTo(LaneToken(runs["Full"], "ride#5")));
        // Split kicks show two pedal lanes (double kick on the second); bars layouts show none.
        Assert.That(LaneToken(runs["SplitKicks"], "kick#1"), Is.Not.EqualTo(LaneToken(runs["SplitKicks"], "kick#0")));
        Assert.That(LaneToken(runs["SplitKicks"], "kick#2"), Is.EqualTo(LaneToken(runs["SplitKicks"], "kick#0")));
        Assert.That(LaneToken(runs["Compact"], "kick#0"), Is.EqualTo("bars"));

        // The baseline run must actually have scored notes and assisted the optional
        // pedal, so the cross-layout equality below compares real play results.
        var baseline = runs["Compact"];
        Assert.Multiple(() =>
        {
            Assert.That(baseline.TotalNotes, Is.EqualTo(9));
            Assert.That(baseline.NotesHit, Is.EqualTo(9));
            Assert.That(baseline.CommittedScore, Is.GreaterThanOrEqualTo(475));
            Assert.That(baseline.Overhits, Is.Zero);
            Assert.That(baseline.OptionalPedalNotes, Is.EqualTo(1));
            Assert.That(baseline.AssistedPedalNotes, Is.EqualTo(1));
            Assert.That(baseline.EngineEvents.Count(e => e.StartsWith("noteHit ", StringComparison.Ordinal)),
                Is.EqualTo(9));
        });

        // Identical inputs through the real engine produce identical events, score,
        // hit state and pedal state under every layout, and every layout's run leaves
        // the authored chart identity untouched.
        var canonicalIdentity = BuildChart().Notes.Select(IdentityOf).ToList();
        foreach (var (name, _) in Layouts().Where(layout => layout.Name != "Compact"))
        {
            var run = runs[name];
            Assert.Multiple(() =>
            {
                Assert.That(run.EngineEvents, Is.EqualTo(baseline.EngineEvents), $"{name} engine events");
                Assert.That(run.StatsSnapshot, Is.EqualTo(baseline.StatsSnapshot), $"{name} stats");
                Assert.That(run.NoteStates, Is.EqualTo(baseline.NoteStates), $"{name} note states");
                Assert.That(run.NoteIdentity, Is.EqualTo(canonicalIdentity), $"{name} chart identity");
                Assert.That(run.Routing, Is.Not.EqualTo(baseline.Routing),
                    $"{name} must route presentation differently from Compact");
            });
        }
    }

    [Test]
    public void ResolverAndEditorEditsNeverRewriteChartNoteIdentity()
    {
        var (_, notes) = BuildChart();
        var identity = notes.Select(IdentityOf).ToList();
        var original = EliteDrumsLayoutPreset.Compact;
        string Slots(EliteDrumsLayoutPreset preset) => string.Join("|",
            preset.Slots.Select(slot => $"{slot.Kind}:{slot.Drum}/{slot.Cymbal}"));
        var originalSlots = Slots(original);

        // Real editor sequences covering pairing, kick combine/split, column moves and
        // removals; several produce incomplete (runtime-unsupported) layouts.
        var edits = new List<Func<EliteDrumsLayoutPreset, EliteDrumsLayoutPreset>>
        {
            preset => EliteDrumsLayoutEditor.Place(preset, 3, "snare"),
            preset => EliteDrumsLayoutEditor.PlaceKick(preset, 8, EliteDrumsLayoutSlotKind.Kick),
            preset => EliteDrumsLayoutEditor.PlaceKick(
                EliteDrumsLayoutEditor.PlaceKick(preset, 8, EliteDrumsLayoutSlotKind.Kick),
                9, EliteDrumsLayoutSlotKind.Kick2),
            preset => EliteDrumsLayoutEditor.MoveColumn(preset, 2, 9),
            preset => EliteDrumsLayoutEditor.Clear(preset, 2),
        };
        foreach (var edit in edits)
        {
            var edited = edit(original);
            bool supported = EliteDrumsLayoutResolver.TryResolve(edited, out var resolved);
            Assert.That(supported, Is.EqualTo(EliteDrumsLayoutValidator.Validate(edited).IsRuntimeSupported));
            Assert.That(notes.Select(IdentityOf).ToList(), Is.EqualTo(identity),
                "layout edits and resolution must not touch chart note identity");
            if (supported) Assert.That(resolved!.Lanes.Count, Is.GreaterThan(0));
        }
        Assert.That(Slots(original), Is.EqualTo(originalSlots), "editor operations must stay detached");

        // Split presentation re-routes kicks to kick1/kick2 lanes while the chart keeps
        // its single physical kick identity.
        var split = edits[2](EliteDrumsLayoutPreset.Full);
        var splitResolved = EliteDrumsLayoutResolver.Resolve(split);
        Assert.Multiple(() =>
        {
            Assert.That(splitResolved.SourceToLane.Keys, Does.Contain("kick1"));
            Assert.That(splitResolved.SourceToLane.Keys, Does.Contain("kick2"));
            Assert.That(splitResolved.SourceToLane.Keys, Does.Not.Contain("kick"));
            Assert.That(splitResolved.SourceToLane["kick1"], Is.Not.EqualTo(splitResolved.SourceToLane["kick2"]));
        });
        var kicks = notes.Where(note => note.Pad == (int) EliteDrumNote.EliteDrumPad.Kick).ToList();
        var sourceIds = notes.Select(note => note.SourceDefinition?.SourceId).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(kicks.Select(note => note.SourceDefinition!.SourceId), Is.All.EqualTo("kick"));
            Assert.That(kicks.Select(note => note.SourceDefinition!.PhysicalOrdinal),
                Is.EqualTo(new[] { 0, 1, 2, 7 }));
            Assert.That(sourceIds, Has.None.EqualTo("kick1"));
            Assert.That(sourceIds, Has.None.EqualTo("kick2"));
        });
    }

    [Test]
    public void SplitKicks_AddNoNewPhysicalKickIdentityOrScoredEvents()
    {
        var combined = EliteDrumsLayoutEditor.PlaceKick(
            EliteDrumsLayoutPreset.Full, 8, EliteDrumsLayoutSlotKind.Kick);
        var split = EliteDrumsLayoutEditor.PlaceKick(combined, 9, EliteDrumsLayoutSlotKind.Kick2);
        var cases = new (string Name, EliteDrumsLayoutPreset Preset)[]
        {
            ("Bars", EliteDrumsLayoutPreset.Full),
            ("Combined", combined),
            ("Split", split),
        };
        Assert.That(EliteDrumsLayoutResolver.Resolve(EliteDrumsLayoutPreset.Full).KickMode,
            Is.EqualTo(EliteDrumsLayoutKickMode.Bars));
        Assert.That(EliteDrumsLayoutResolver.Resolve(combined).KickMode,
            Is.EqualTo(EliteDrumsLayoutKickMode.Combined));
        Assert.That(EliteDrumsLayoutResolver.Resolve(split).KickMode,
            Is.EqualTo(EliteDrumsLayoutKickMode.Split));

        var runs = cases.ToDictionary(
            kickCase => kickCase.Name, kickCase => Run(BuildKickChart(), KickInputs, kickCase.Preset, false));

        // The baseline actually scores every kick flavor and the snare with the single
        // physical kick action the engine defines.
        var baseline = runs["Bars"];
        Assert.Multiple(() =>
        {
            Assert.That(baseline.TotalNotes, Is.EqualTo(4));
            Assert.That(baseline.NotesHit, Is.EqualTo(4));
            Assert.That(baseline.CommittedScore, Is.EqualTo(200));
            Assert.That(baseline.Overhits, Is.Zero);
            Assert.That(baseline.EngineEvents.Count(e => e.StartsWith("pad Kick hit=True", StringComparison.Ordinal)),
                Is.EqualTo(3));
        });

        var canonicalIdentity = BuildKickChart().Notes.Select(IdentityOf).ToList();
        foreach (var (name, _) in cases.Where(kickCase => kickCase.Name != "Bars"))
        {
            var run = runs[name];
            Assert.Multiple(() =>
            {
                Assert.That(run.EngineEvents, Is.EqualTo(baseline.EngineEvents), $"{name} engine events");
                Assert.That(run.StatsSnapshot, Is.EqualTo(baseline.StatsSnapshot), $"{name} stats");
                Assert.That(run.NoteStates, Is.EqualTo(baseline.NoteStates), $"{name} note states");
                Assert.That(run.NoteIdentity, Is.EqualTo(canonicalIdentity), $"{name} chart identity");
                Assert.That(run.Routing, Is.Not.EqualTo(baseline.Routing),
                    $"{name} must route kick presentation differently from bars");
            });
        }

        // Splitting kicks adds no new physical kick identity or scored events: the chart
        // still holds exactly three kick-pad notes with kick source IDs and ordinals.
        Assert.Multiple(() =>
        {
            Assert.That(runs["Split"].NoteIdentity.Count(
                identity => identity.Contains("pad=" + (int) EliteDrumNote.EliteDrumPad.Kick, StringComparison.Ordinal)),
                Is.EqualTo(3));
            Assert.That(runs["Split"].NoteIdentity, Has.None.Contains("kick1"));
            Assert.That(runs["Split"].NoteIdentity, Has.None.Contains("kick2"));
            Assert.That(runs["Split"].EngineEvents.Count(e => e.StartsWith("noteHit ", StringComparison.Ordinal)),
                Is.EqualTo(4));
            Assert.That(runs["Split"].TotalNotes, Is.EqualTo(runs["Bars"].TotalNotes));
        });
    }
}
