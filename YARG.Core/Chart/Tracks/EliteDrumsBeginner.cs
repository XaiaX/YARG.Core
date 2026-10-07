using System;
using System.Collections.Generic;

namespace YARG.Core.Chart
{
    /// <summary>Builds native Beginner gameplay from a selected, kick-filtered Easy chart.</summary>
    public static class EliteDrumsBeginner
    {
        public static InstrumentDifficulty<EliteDrumNote> Collapse(InstrumentDifficulty<EliteDrumNote> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var result = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, Difficulty.Beginner);
            var onsets = new SortedDictionary<uint, (double Time, NoteFlags Flags, DrumNoteFlags DrumFlags)>();
            const NoteFlags boundaries = NoteFlags.StarPower | NoteFlags.StarPowerStart | NoteFlags.StarPowerEnd |
                NoteFlags.Solo | NoteFlags.SoloStart | NoteFlags.SoloEnd | NoteFlags.CodaStart |
                NoteFlags.CodaEnd | NoteFlags.BigRockEnding;
            foreach (var parent in source.Notes)
            {
                foreach (var note in parent.AllNotes)
                {
                    if (note.IsInvisibleTerminator) continue;
                    var flags = note.Flags & boundaries;
                    var drumFlags = note.DrumFlags & DrumNoteFlags.StarPowerActivator;
                    if (onsets.TryGetValue(note.Tick, out var onset))
                        onsets[note.Tick] = (Math.Min(onset.Time, note.Time), onset.Flags | flags, onset.DrumFlags | drumFlags);
                    else
                        onsets.Add(note.Tick, (note.Time, flags, drumFlags));
                }
            }
            EliteDrumNote? previous = null;
            foreach (var onset in onsets)
            {
                var note = new EliteDrumNote(EliteDrumNote.EliteDrumPad.Wildcard, DrumNoteType.Neutral,
                    EliteDrumNote.EliteDrumsHatState.Indifferent, EliteDrumNote.EliteDrumsHatPedalType.Stomp,
                    false, onset.Value.DrumFlags, onset.Value.Flags, EliteDrumNote.EliteDrumsChannelFlag.None,
                    onset.Value.Time, onset.Key, false);
                note.PreviousNote = previous;
                if (previous != null) previous.NextNote = note;
                result.Notes.Add(note);
                previous = note;
            }
            foreach (var phrase in source.Phrases)
            {
                if (phrase.Type is PhraseType.TremoloLane or PhraseType.TrillLane or PhraseType.KickLane ||
                    phrase.Type >= PhraseType.EliteDrums_RightCrashLane && phrase.Type <= PhraseType.EliteDrums_HatPedalLane)
                    continue;
                result.Phrases.Add(phrase.Clone());
            }
            foreach (var text in source.TextEvents) result.TextEvents.Add(text.Clone());
            foreach (var shift in source.RangeShiftEvents) result.RangeShiftEvents.Add(shift.Clone());
            // A new difficulty deliberately carries no kit-specific lane records or descriptors.
            return result;
        }
    }
}
