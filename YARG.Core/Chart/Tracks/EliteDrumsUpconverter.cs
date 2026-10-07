using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Extensions;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.Chart
{
    /// <summary>Deterministic, direct conversion of native Pro/Four-Lane drums to typed Elite drums.</summary>
    public static class EliteDrumsUpconverter
    {
        /// <summary>
        /// Converts one selected difficulty without changing its source. Cymbal and tom identities
        /// are mapped directly; no articulation, pedal, flam, or missing lane members are inferred.
        /// Lane membership uses the source loader's validated Tremolo/Trill/KickLane flags.
        /// Callers must select an actual native source, not an Elite-generated downchart or
        /// a Pro/Four-Lane representation synthesized from native Five-Lane drums.
        /// Native Five-Lane uses its own direct pad mapping.
        /// </summary>
        public static InstrumentDifficulty<EliteDrumNote> ConvertToEliteDrums(
            this InstrumentDifficulty<DrumNote> source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (source.Instrument is not (Instrument.ProDrums or Instrument.FourLaneDrums or Instrument.FiveLaneDrums))
                throw new ArgumentException("Only native classic drums can be converted to Elite drums.", nameof(source));

            var result = new InstrumentDifficulty<EliteDrumNote>(Instrument.EliteDrums, source.Difficulty);
            var sourceMembers = new Dictionary<EliteDrumSourceDefinition, DrumNote>();
            int ordinal = 0;
            foreach (var chord in source.Notes)
            {
                EliteDrumNote? parent = null;
                foreach (var member in chord.AllNotes)
                {
                    if (member.ConversionOrigin is not null)
                        throw new ArgumentException("Elite-generated downcharts are not native conversion sources.", nameof(source));
                    var pad = source.Instrument == Instrument.FiveLaneDrums
                        ? MapFiveLanePad(member.Pad) : MapPad(member.Pad);
                    var definition = new EliteDrumSourceDefinition(
                        $"{source.Instrument}:{source.Difficulty}:{ordinal}", ordinal++, (int)pad,
                        member.Tick, member.TickLength, member.Time, member.TimeLength);
                    sourceMembers.Add(definition, member);
                    var converted = new EliteDrumNote(pad, member.Type, EliteDrumsHatState.Indifferent,
                        EliteDrumsHatPedalType.Stomp, false,
                        member.DrumFlags & ~(DrumNoteFlags.KickLane | DrumNoteFlags.KickLaneStart | DrumNoteFlags.KickLaneEnd),
                        member.Flags & ~(NoteFlags.Tremolo | NoteFlags.Trill | NoteFlags.LaneStart | NoteFlags.LaneEnd),
                        EliteDrumsChannelFlag.None, member.Time, member.Tick, member.IsDoubleKick, definition);
                    if (parent is null) parent = converted;
                    else parent.AddChildNote(converted);
                }
                if (parent is not null) result.Notes.Add(parent);
            }

            for (int index = 0; index < result.Notes.Count; index++)
            {
                foreach (var member in result.Notes[index].AllNotes)
                {
                    member.PreviousNote = index > 0 ? result.Notes[index - 1] : null;
                    member.NextNote = index + 1 < result.Notes.Count ? result.Notes[index + 1] : null;
                }
            }

            var gems = result.Notes.SelectMany(note => note.ChildNotes.Prepend(note)).ToList();
            var records = new List<EliteDrumNativeAuthoredLaneRecord>();
            foreach (var phrase in source.Phrases)
            {
                if (phrase.Type is not (PhraseType.TremoloLane or PhraseType.TrillLane or PhraseType.KickLane))
                {
                    result.Phrases.Add(phrase.Clone());
                    continue;
                }

                // A legacy hand lane can contain different cymbal/tom identities on the same
                // color. Retain every actual member identity rather than choosing a likely kit piece.
                var members = gems.Where(note => note.Tick >= phrase.Tick && note.Tick < phrase.TickEnd
                    && (phrase.Type switch
                    {
                        PhraseType.KickLane => note.Pad == (int)EliteDrumPad.Kick
                            && sourceMembers[note.SourceDefinition!].IsKickLane,
                        PhraseType.TremoloLane => note.Pad != (int)EliteDrumPad.Kick
                            && sourceMembers[note.SourceDefinition!].IsTremolo,
                        PhraseType.TrillLane => note.Pad != (int)EliteDrumPad.Kick
                            && sourceMembers[note.SourceDefinition!].IsTrill,
                        _ => false
                    }));
                foreach (var group in members.GroupBy(note => (EliteDrumPad)note.Pad).OrderBy(group => group.Key))
                {
                    var type = MapLane(group.Key);
                    int phraseOrdinal = result.Phrases.Count;
                    result.Phrases.Add(new Phrase(type, phrase.Time, phrase.TimeLength, phrase.Tick, phrase.TickLength));
                    records.Add(new EliteDrumNativeAuthoredLaneRecord(phraseOrdinal, type, phrase.Tick,
                        phrase.TickEnd, group.Select(note => note.SourceDefinition!)));
                }
            }
            result.SetEliteDrumNativeAuthoredLaneRecords(records);
            result.TextEvents.AddRange(source.TextEvents.Duplicate());
            result.RangeShiftEvents.AddRange(source.RangeShiftEvents.Duplicate());
            return result;
        }

        private static EliteDrumPad MapFiveLanePad(int pad) => (FiveLaneDrumPad)pad switch
        {
            FiveLaneDrumPad.Kick => EliteDrumPad.Kick,
            FiveLaneDrumPad.Red => EliteDrumPad.Snare,
            FiveLaneDrumPad.Yellow => EliteDrumPad.HiHat,
            FiveLaneDrumPad.Orange => EliteDrumPad.RightCrash,
            FiveLaneDrumPad.Blue => EliteDrumPad.Tom2,
            FiveLaneDrumPad.Green => EliteDrumPad.Tom3,
            _ => throw new ArgumentException($"Unsupported native Five-Lane drum pad: {pad}.")
        };

        private static EliteDrumPad MapPad(int pad) => (FourLaneDrumPad)pad switch
        {
            FourLaneDrumPad.Kick => EliteDrumPad.Kick,
            FourLaneDrumPad.RedDrum => EliteDrumPad.Snare,
            FourLaneDrumPad.YellowCymbal => EliteDrumPad.HiHat,
            FourLaneDrumPad.BlueCymbal => EliteDrumPad.Ride,
            FourLaneDrumPad.GreenCymbal => EliteDrumPad.RightCrash,
            FourLaneDrumPad.YellowDrum => EliteDrumPad.Tom1,
            FourLaneDrumPad.BlueDrum => EliteDrumPad.Tom2,
            FourLaneDrumPad.GreenDrum => EliteDrumPad.Tom3,
            _ => throw new ArgumentException($"Unsupported native drum pad: {pad}.")
        };

        private static PhraseType MapLane(EliteDrumPad pad) => pad switch
        {
            EliteDrumPad.Kick => PhraseType.EliteDrums_KickLane,
            EliteDrumPad.Snare => PhraseType.EliteDrums_SnareLane,
            EliteDrumPad.HiHat => PhraseType.EliteDrums_HiHatLane,
            EliteDrumPad.Ride => PhraseType.EliteDrums_RideLane,
            EliteDrumPad.RightCrash => PhraseType.EliteDrums_RightCrashLane,
            EliteDrumPad.Tom1 => PhraseType.EliteDrums_Tom1Lane,
            EliteDrumPad.Tom2 => PhraseType.EliteDrums_Tom2Lane,
            EliteDrumPad.Tom3 => PhraseType.EliteDrums_Tom3Lane,
            _ => throw new ArgumentOutOfRangeException(nameof(pad))
        };
    }
}
