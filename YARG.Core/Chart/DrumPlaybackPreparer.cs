using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YARG.Core.Game;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.Chart
{
    /// <summary>Player-owned playable notes reconstructed from the exact recorded authored source.</summary>
    public sealed class PreparedDrumPlayback
    {
        public InstrumentDifficulty<DrumNote>? Classic { get; }
        public InstrumentDifficulty<EliteDrumNote>? Elite { get; }
        internal PreparedDrumPlayback(InstrumentDifficulty<DrumNote> classic) => Classic = classic;
        internal PreparedDrumPlayback(InstrumentDifficulty<EliteDrumNote> elite) => Elite = elite;
    }

    public static class DrumPlaybackPreparer
    {
        /// <summary>
        /// Applies recorded kick policy, converts locally, applies usual player modifiers, rebuilds
        /// controls and links, then derives Beginner. Use this order for both live and replay.
        /// </summary>
        public static PreparedDrumPlayback Prepare(SongChart chart, ResolvedDrumPlayback playback,
            Action<InstrumentDifficulty<DrumNote>>? classicModifiers = null,
            Action<InstrumentDifficulty<EliteDrumNote>>? eliteModifiers = null)
        {
            if (chart is null) throw new ArgumentNullException(nameof(chart));
            if (playback is null) throw new ArgumentNullException(nameof(playback));
            if (!chart.AuthoredDrumSources.TryGetTier(playback.SourceFormat, playback.SourceDifficulty, out var source))
                throw new InvalidDataException("The recorded authored drum source tier is missing.");
            bool beginner = playback.BaseDifficulty == Difficulty.Beginner;
            if (playback.SourceFormat == DrumSourceFormat.Elite)
            {
                var elite = source!.CloneEliteDifficulty();
                Filter(elite, n => !(n.Pad == (int)EliteDrumPad.Kick && n.IsDoubleKick && playback.ExtraKickPolicy == DrumExtraKickPolicy.Remove), n =>
                {
                    if (n.Pad != (int)EliteDrumPad.Kick) return n;
                    bool simplify = playback.ExtraKickPolicy != DrumExtraKickPolicy.Include;
                    bool flam = simplify && source.PairedKickTicks.Contains(n.Tick) ? false : n.IsFlam;
                    return new EliteDrumNote((EliteDrumPad)n.Pad, n.Dynamics, n.HatState, n.HatPedalType,
                        flam, n.DrumFlags, n.Flags, n.ChannelFlag, n.Time, n.Tick,
                        playback.ExtraKickPolicy == DrumExtraKickPolicy.NormalizeExtraOnly ? false : n.IsDoubleKick,
                        n.SourceDefinition, n.IsFlatFlam, simplify && source.PairedKickTicks.Contains(n.Tick) ? false : n.IsAuthoredFlam);
                });
                Rebuild(elite);
                if (playback.RequestedOutput == Instrument.EliteDrums)
                    return FinishElite(elite, beginner, chart, eliteModifiers);
                var classic = MoonSongLoader.ConvertPlaybackElite(elite, playback.RequestedOutput, chart.Resolution);
                foreach (var note in classic.Notes.SelectMany(n => n.ChildNotes.Prepend(n)))
                    note.Time = chart.SyncTrack.TickToTime(note.Tick);
                return FinishClassic(classic, beginner, chart, classicModifiers);
            }
            else
            {
                var classic = source!.CloneClassicDifficulty();
                Filter(classic, n => !(n.IsDoubleKick && playback.ExtraKickPolicy == DrumExtraKickPolicy.Remove), n =>
                    new DrumNote(n.Pad, n.Type, n.DrumFlags, n.Flags, n.Time, n.Tick,
                        playback.ExtraKickPolicy == DrumExtraKickPolicy.NormalizeExtraOnly ? false : n.IsDoubleKick,
                        n.Stem, n.ConversionOrigin));
                Rebuild(classic);
                if (playback.RequestedOutput == Instrument.EliteDrums)
                    return FinishElite(classic.ConvertToEliteDrums(), beginner, chart, eliteModifiers);
                var converted = MoonSongLoader.ConvertPlaybackClassic(classic, playback.RequestedOutput, false, chart.Resolution);
                foreach (var note in converted.Notes.SelectMany(n => n.ChildNotes.Prepend(n)))
                    note.Time = chart.SyncTrack.TickToTime(note.Tick);
                return FinishClassic(converted, beginner, chart, classicModifiers);
            }
        }

        private static PreparedDrumPlayback FinishClassic(InstrumentDifficulty<DrumNote> notes, bool beginner,
            SongChart chart, Action<InstrumentDifficulty<DrumNote>>? modifiers)
        {
            var sync = chart.SyncTrack;
            ApplyModifiers(notes, modifiers);
            chart.GeneratePlaybackActivationPhrases(notes);
            Rebuild(notes);
            if (beginner)
            {
                notes = MoonSongLoader.ConvertPlaybackClassic(notes, notes.Instrument, true, sync.Resolution);
                foreach (var note in notes.Notes.SelectMany(n => n.ChildNotes.Prepend(n)))
                    note.Time = sync.TickToTime(note.Tick);
                Rebuild(notes);
            }
            return new(notes);
        }

        private static PreparedDrumPlayback FinishElite(InstrumentDifficulty<EliteDrumNote> notes, bool beginner,
            SongChart chart, Action<InstrumentDifficulty<EliteDrumNote>>? modifiers)
        {
            ApplyModifiers(notes, modifiers);
            var controls = new InstrumentDifficulty<DrumNote>(Instrument.ProDrums, notes.Difficulty);
            controls.Phrases.AddRange(notes.Phrases.Select(p => p.Clone()));
            foreach (var onset in notes.Notes)
                controls.Notes.Add(new DrumNote(FourLaneDrumPad.RedDrum, DrumNoteType.Neutral,
                    DrumNoteFlags.None, onset.Flags, onset.Time, onset.Tick));
            chart.GeneratePlaybackActivationPhrases(controls);
            foreach (var phrase in controls.Phrases.Where(p => p.Type == PhraseType.DrumFill))
                if (!notes.Phrases.Any(p => p.Type == phrase.Type && p.Tick == phrase.Tick && p.TickEnd == phrase.TickEnd))
                    notes.Phrases.Add(phrase.Clone());
            Rebuild(notes);
            if (beginner) notes = EliteDrumsBeginner.Collapse(notes);
            return new(notes);
        }

        private static void ApplyModifiers<T>(InstrumentDifficulty<T> notes, Action<InstrumentDifficulty<T>>? modifiers)
            where T : Note<T>
        {
            var starts = notes.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Where(n => n.IsCodaStart).Select(n => n.Tick).Distinct().ToList();
            var ends = notes.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).Where(n => n.IsCodaEnd).Select(n => n.Tick).Distinct().ToList();
            modifiers?.Invoke(notes);
            foreach (var tick in starts)
            {
                var target = notes.Notes.FirstOrDefault(n => n.Tick >= tick);
                if (target is not null) foreach (var member in target.AllNotes) member.ActivateFlag(NoteFlags.CodaStart);
            }
            foreach (var tick in ends)
            {
                var target = notes.Notes.LastOrDefault(n => n.Tick <= tick);
                if (target is not null) foreach (var member in target.AllNotes) member.ActivateFlag(NoteFlags.CodaEnd);
            }
        }

        private static void Filter<T>(InstrumentDifficulty<T> notes, Func<T, bool> keep, Func<T, T> transform)
            where T : Note<T>
        {
            var codaStarts = notes.Notes.Where(n => n.IsCodaStart).Select(n => n.Tick).ToList();
            var codaEnds = notes.Notes.Where(n => n.IsCodaEnd).Select(n => n.Tick).ToList();
            var chords = new List<T>();
            foreach (var chord in notes.Notes)
            {
                T? parent = null;
                foreach (var member in chord.ChildNotes.Prepend(chord).Where(keep))
                {
                    var note = transform(member).CloneWithoutChildNotes();
                    if (parent is null) parent = note;
                    else parent.AddChildNote(note);
                }
                if (parent is not null) chords.Add(parent);
            }
            notes.Notes.Clear();
            notes.Notes.AddRange(chords);
            foreach (var tick in codaStarts)
            {
                var target = chords.FirstOrDefault(n => n.Tick >= tick);
                if (target is not null) foreach (var member in target.AllNotes) member.ActivateFlag(NoteFlags.CodaStart);
            }
            foreach (var tick in codaEnds)
            {
                var target = chords.LastOrDefault(n => n.Tick <= tick);
                if (target is not null) foreach (var member in target.AllNotes) member.ActivateFlag(NoteFlags.CodaEnd);
            }
        }

        /// <summary>Re-establishes control boundaries and chord links after player-local filtering.</summary>
        public static void Rebuild<T>(InstrumentDifficulty<T> difficulty) where T : Note<T>
        {
            for (int i = 0; i < difficulty.Notes.Count; i++)
                foreach (var note in difficulty.Notes[i].AllNotes)
                {
                    note.PreviousNote = i > 0 ? difficulty.Notes[i - 1] : null;
                    note.NextNote = i + 1 < difficulty.Notes.Count ? difficulty.Notes[i + 1] : null;
                    if (note is DrumNote drum) drum.ClearFlag(DrumNoteFlags.StarPowerActivator | DrumNoteFlags.KickLaneStart | DrumNoteFlags.KickLaneEnd);
                    if (note is EliteDrumNote eliteDrum)
                        eliteDrum.DrumFlags &= ~(DrumNoteFlags.StarPowerActivator | DrumNoteFlags.KickLaneStart | DrumNoteFlags.KickLaneEnd);
                    note.ClearFlag(NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd |
                        NoteFlags.Solo | NoteFlags.SoloStart | NoteFlags.SoloEnd | NoteFlags.BigRockEnding |
                        NoteFlags.LaneStart | NoteFlags.LaneEnd);
                }
            if (difficulty is InstrumentDifficulty<DrumNote> generatedDifficulty && generatedDifficulty.EliteDrumAuthoredLanePhraseRecords.Count > 0)
            {
                var gems = generatedDifficulty.Notes.SelectMany(n => n.ChildNotes.Prepend(n)).ToList();
                foreach (var record in generatedDifficulty.EliteDrumAuthoredLanePhraseRecords)
                {
                    var lane = gems.Where(n => n.ConversionOrigin is not null && record.Origins.Contains(n.ConversionOrigin) && n.IsLane).ToList();
                    if (lane.Count >= 2)
                    {
                        lane[0].ActivateFlag(NoteFlags.LaneStart);
                        lane[lane.Count - 1].ActivateFlag(NoteFlags.LaneEnd);
                    }
                }
            }
            if (difficulty is InstrumentDifficulty<EliteDrumNote> eliteDifficulty)
            {
                var survivors = eliteDifficulty.Notes.SelectMany(n => n.ChildNotes.Prepend(n))
                    .Where(n => n.SourceDefinition is not null).ToList();
                var records = new List<EliteDrumNativeAuthoredLaneRecord>();
                foreach (var record in eliteDifficulty.EliteDrumNativeAuthoredLaneRecords)
                {
                    var members = survivors.Where(n => record.MemberSources.Contains(n.SourceDefinition!)).ToList();
                    records.Add(new EliteDrumNativeAuthoredLaneRecord(record.PhraseOrdinal, record.LaneType,
                        record.StartTick, record.EndTick, members.Select(n => n.SourceDefinition!)));
                    if (members.Count >= 2)
                    {
                        foreach (var member in members) member.ActivateFlag(NoteFlags.Tremolo);
                        members[0].ActivateFlag(NoteFlags.LaneStart);
                        members[members.Count - 1].ActivateFlag(NoteFlags.LaneEnd);
                    }
                }
                eliteDifficulty.SetEliteDrumNativeAuthoredLaneRecords(records);
            }
            foreach (var phrase in difficulty.Phrases)
            {
                bool inclusive = phrase.Type == PhraseType.BigRockEnding;
                var chords = difficulty.Notes.Where(n => n.Tick >= phrase.Tick &&
                    (inclusive ? n.Tick <= phrase.TickEnd : n.Tick < phrase.TickEnd)).ToList();
                if (chords.Count == 0) continue;
                var control = phrase.Type switch
                {
                    PhraseType.StarPower => (NoteFlags.StarPower, NoteFlags.StarPowerStart, NoteFlags.StarPowerEnd),
                    PhraseType.Solo => (NoteFlags.Solo, NoteFlags.SoloStart, NoteFlags.SoloEnd),
                    PhraseType.BigRockEnding => (NoteFlags.BigRockEnding, NoteFlags.None, NoteFlags.None),
                    _ => (NoteFlags.None, NoteFlags.None, NoteFlags.None)
                };
                foreach (var chord in chords)
                    foreach (var note in chord.AllNotes) note.ActivateFlag(control.Item1);
                foreach (var note in chords[0].AllNotes) note.ActivateFlag(control.Item2);
                foreach (var note in chords[chords.Count - 1].AllNotes) note.ActivateFlag(control.Item3);
                if (phrase.Type is PhraseType.TremoloLane or PhraseType.TrillLane)
                {
                    var lane = chords.SelectMany(n => n.ChildNotes.Prepend(n)).Where(n => phrase.Type == PhraseType.TremoloLane ? n.IsTremolo : n.IsTrill).ToList();
                    if (lane.Count >= 2)
                    {
                        lane[0].ActivateFlag(NoteFlags.LaneStart);
                        lane[lane.Count - 1].ActivateFlag(NoteFlags.LaneEnd);
                    }
                    else
                    {
                        foreach (var member in lane) member.ClearFlag(NoteFlags.Tremolo | NoteFlags.Trill);
                    }
                }
                if (phrase.Type == PhraseType.KickLane)
                {
                    var kicks = chords.SelectMany(n => n.ChildNotes.Prepend(n)).OfType<DrumNote>().Where(n => n.IsKickLane).ToList();
                    if (kicks.Count >= 2)
                    {
                        kicks[0].ActivateFlag(DrumNoteFlags.KickLaneStart);
                        kicks[kicks.Count - 1].ActivateFlag(DrumNoteFlags.KickLaneEnd);
                    }
                }
                if (phrase.Type == PhraseType.DrumFill)
                {
                    var activator = chords[chords.Count - 1].ChildNotes.Prepend(chords[chords.Count - 1]).Last();
                    if (activator is DrumNote classic) classic.ActivateFlag(DrumNoteFlags.StarPowerActivator);
                    if (activator is EliteDrumNote elite) elite.ActivateFlag(DrumNoteFlags.StarPowerActivator);
                }
            }
        }
    }
}
