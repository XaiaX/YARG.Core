using MoonscraperChartEditor.Song;
using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Parsing;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.Chart
{
    internal partial class MoonSongLoader : ISongLoader
    {
        private int _eliteSourceOrdinal;

        public InstrumentTrack<EliteDrumNote> LoadEliteDrumsTrack(Instrument instrument)
        {
            return instrument.ToNativeGameMode() is GameMode.EliteDrums ?
                LoadEliteDrumsTrack(instrument, CreateEliteDrumNote) :
                throw new ArgumentException($"Instrument {instrument} is not Elite Drums!", nameof(instrument));
        }

        private InstrumentTrack<EliteDrumNote> LoadEliteDrumsTrack(Instrument instrument, CreateNoteDelegate<EliteDrumNote> createNote)
        {
            _eliteSourceOrdinal = 0;
            var difficulties = new Dictionary<Difficulty, InstrumentDifficulty<EliteDrumNote>>()
            {
                { Difficulty.Easy, LoadNativeEliteDrumsDifficulty(instrument, Difficulty.Easy, createNote) },
                { Difficulty.Medium, LoadNativeEliteDrumsDifficulty(instrument, Difficulty.Medium, createNote) },
                { Difficulty.Hard, LoadNativeEliteDrumsDifficulty(instrument, Difficulty.Hard, createNote) },
                { Difficulty.Expert, LoadNativeEliteDrumsDifficulty(instrument, Difficulty.Expert, createNote) },
                { Difficulty.ExpertPlus, LoadNativeEliteDrumsDifficulty(instrument, Difficulty.ExpertPlus, createNote) },
            };
            return new(instrument, difficulties);
        }

        private InstrumentDifficulty<EliteDrumNote> LoadEliteDrumsDifficulty(Instrument instrument,
            Difficulty difficulty, CreateNoteDelegate<EliteDrumNote> createNote)
        {
            return LoadDifficulty(instrument, difficulty, createNote, HandleEliteDrumsTextEvent);
        }

        private InstrumentDifficulty<EliteDrumNote> LoadNativeEliteDrumsDifficulty(Instrument instrument,
            Difficulty difficulty, CreateNoteDelegate<EliteDrumNote> createNote)
        {
            var chart = LoadEliteDrumsDifficulty(instrument, difficulty, createNote);
            var records = new List<EliteDrumNativeAuthoredLaneRecord>();
            for (int index = 0; index < chart.Phrases.Count; index++)
            {
                var phrase = chart.Phrases[index];
                if (!EliteDrumNativeAuthoredLaneRecord.TryGetNativePad(phrase.Type, out var pad)) continue;
                var sources = chart.Notes.SelectMany(note => note.ChildNotes.Prepend(note))
                    .Where(note => note.Pad == (int) pad && note.Tick >= phrase.Tick && note.Tick < phrase.TickEnd)
                    .Select(note => note.SourceDefinition).OfType<EliteDrumSourceDefinition>();
                records.Add(new EliteDrumNativeAuthoredLaneRecord(index, phrase.Type,
                    phrase.Tick, phrase.TickEnd, sources));
            }
            chart.SetEliteDrumNativeAuthoredLaneRecords(records);
            return chart;
        }

        private EliteDrumNote CreateEliteDrumNote(MoonNote moonNote, Dictionary<MoonPhrase.Type, MoonPhrase> currentPhrases,
            List<EliteDrumNote> notes)
        {
            var pad = GetEliteDrumPad(moonNote);
            var noteDynamics = GetEliteDrumNoteDynamics(moonNote);
            var hatPedalType = GetEliteDrumHatPedalType(moonNote);
            var hatState = GetEliteDrumHatState(moonNote);
            // A paired 1x+2x kick remains an ordinary 1x kick on Expert;
            // only Expert+ presents the combined kick as a flam.
            var inSamePadAuthoredRollLane = IsInSamePadAuthoredRollLane(moonNote, pad, currentPhrases);
            var isAuthoredFlam = GetEliteDrumNoteIsFlam(moonNote);
            var isFlam = isAuthoredFlam &&
                (pad is not EliteDrumPad.Kick || _currentDifficulty == Difficulty.ExpertPlus) &&
                (pad is EliteDrumPad.Kick or EliteDrumPad.HatPedal || !inSamePadAuthoredRollLane);
            var isFlatFlam = GetEliteDrumNoteIsFlatFlam(moonNote) &&
                pad is not EliteDrumPad.Kick and not EliteDrumPad.HatPedal && !inSamePadAuthoredRollLane;
            var drumFlags = GetDrumNoteFlags(moonNote, currentPhrases);
            var generalFlags = GetGeneralFlags(moonNote, currentPhrases);
            var channelFlag = GetEliteDrumsChannelFlag(moonNote);

            var isDoubleKick = moonNote.eliteDrumPad is MoonNote.EliteDrumPad.Kick && ((moonNote.flags & MoonNote.Flags.InstrumentPlus) != 0);

            double time = _moonSong.TickToTime(moonNote.tick);
            var source = new EliteDrumSourceDefinition(
                $"elite:{_currentDifficulty}:{moonNote.tick}:{moonNote.rawNote}",
                _eliteSourceOrdinal++,
                (int)pad,
                moonNote.tick,
                moonNote.length,
                time,
                _moonSong.TickToTime(moonNote.tick + moonNote.length) - time);
            return new(pad, noteDynamics, hatState, hatPedalType, isFlam, drumFlags, generalFlags, channelFlag, time, moonNote.tick, isDoubleKick, source, isFlatFlam, isAuthoredFlam);
        }

        private void HandleEliteDrumsTextEvent(MoonText text)
        {
            if (TextEvents.TryParseEliteDrumsSnareStemEvent(text.text, out var setting, out var difficulty))
            {
                // Handle [snare_stem <pad> <diff?>] event
                return;
            }

            /* Post-MVP
            if (TextEvents.TryParseEliteDrumsPositionalModeEvent(text.text, out var difficulty, out var cymbals, out var drums))
            {
                // Handle [positional <on/off> <diff?>] event
                return;
            }
            */
        }

        private EliteDrumPad GetEliteDrumPad(MoonNote moonNote)
        {
            return moonNote.eliteDrumPad switch
            {
                MoonNote.EliteDrumPad.HatPedal => EliteDrumPad.HatPedal,
                MoonNote.EliteDrumPad.Kick => EliteDrumPad.Kick,
                MoonNote.EliteDrumPad.Snare => EliteDrumPad.Snare,
                MoonNote.EliteDrumPad.HiHat => EliteDrumPad.HiHat,
                MoonNote.EliteDrumPad.LeftCrash => EliteDrumPad.LeftCrash,
                MoonNote.EliteDrumPad.Tom1 => EliteDrumPad.Tom1,
                MoonNote.EliteDrumPad.Tom2 => EliteDrumPad.Tom2,
                MoonNote.EliteDrumPad.Tom3 => EliteDrumPad.Tom3,
                MoonNote.EliteDrumPad.Ride => EliteDrumPad.Ride,
                MoonNote.EliteDrumPad.RightCrash => EliteDrumPad.RightCrash,
                _ => throw new ArgumentException($"Invalid Moonscraper drum pad {moonNote.drumPad}!", nameof(moonNote))
            };
        }

        private DrumNoteType GetEliteDrumNoteDynamics(MoonNote moonNote)
        {
            var dynamics = DrumNoteType.Neutral;

            // Accents/ghosts
            if ((moonNote.flags & MoonNote.Flags.ProDrums_Accent) != 0)
                dynamics = DrumNoteType.Accent;
            else if ((moonNote.flags & MoonNote.Flags.ProDrums_Ghost) != 0)
                dynamics = DrumNoteType.Ghost;

            return dynamics;
        }

        private EliteDrumsHatState GetEliteDrumHatState(MoonNote moonNote)
        {
            if (moonNote.eliteDrumPad is not MoonNote.EliteDrumPad.HiHat)
            {
                return EliteDrumsHatState.Indifferent;
            }

            var hatState = EliteDrumsHatState.Open;

            if ((moonNote.flags & MoonNote.Flags.EliteDrums_ForcedIndifferent) != 0)
                hatState = EliteDrumsHatState.Indifferent;
            else if ((moonNote.flags & MoonNote.Flags.EliteDrums_ForcedClosed) != 0)
                hatState = EliteDrumsHatState.Closed;

            return hatState;
        }

        private EliteDrumsHatPedalType GetEliteDrumHatPedalType(MoonNote moonNote)
        {
            var hatPedalType = EliteDrumsHatPedalType.Stomp;

            if (moonNote.eliteDrumPad is MoonNote.EliteDrumPad.HatPedal)
            {
                if ((moonNote.flags & MoonNote.Flags.EliteDrums_InvisibleTerminator) != 0)
                    hatPedalType = EliteDrumsHatPedalType.InvisibleTerminator;
                else if ((moonNote.flags & MoonNote.Flags.EliteDrums_Splash) != 0)
                    hatPedalType = EliteDrumsHatPedalType.Splash;
            }

            return hatPedalType;
        }

        private bool GetEliteDrumNoteIsFlam(MoonNote moonNote)
        {
            return (moonNote.flags & MoonNote.Flags.EliteDrums_Flam) != 0;
        }

        private bool GetEliteDrumNoteIsFlatFlam(MoonNote moonNote)
        {
            return (moonNote.flags & MoonNote.Flags.EliteDrums_FlatFlam) != 0;
        }

        private static bool IsInSamePadAuthoredRollLane(MoonNote moonNote, EliteDrumPad pad,
            Dictionary<MoonPhrase.Type, MoonPhrase> currentPhrases)
        {
            foreach (var (laneType, lanePad) in AuthoredRollLanes)
            {
                if (lanePad == pad && currentPhrases.TryGetValue(laneType, out var lane) &&
                    IsEventInPhrase(moonNote, lane))
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly (MoonPhrase.Type Type, EliteDrumPad Pad)[] AuthoredRollLanes =
        {
            (MoonPhrase.Type.EliteDrums_SnareLane, EliteDrumPad.Snare),
            (MoonPhrase.Type.EliteDrums_HiHatLane, EliteDrumPad.HiHat),
            (MoonPhrase.Type.EliteDrums_LeftCrashLane, EliteDrumPad.LeftCrash),
            (MoonPhrase.Type.EliteDrums_Tom1Lane, EliteDrumPad.Tom1),
            (MoonPhrase.Type.EliteDrums_Tom2Lane, EliteDrumPad.Tom2),
            (MoonPhrase.Type.EliteDrums_Tom3Lane, EliteDrumPad.Tom3),
            (MoonPhrase.Type.EliteDrums_RideLane, EliteDrumPad.Ride),
            (MoonPhrase.Type.EliteDrums_RightCrashLane, EliteDrumPad.RightCrash),
        };

        private EliteDrumsChannelFlag GetEliteDrumsChannelFlag(MoonNote moonNote)
        {
            // Kicks are not affected by channel flags
            if (moonNote.eliteDrumPad is MoonNote.EliteDrumPad.Kick)
            {
                return EliteDrumsChannelFlag.None;
            }

            // Only drums can be forced to red
            if (moonNote.eliteDrumPad is MoonNote.EliteDrumPad.Snare or MoonNote.EliteDrumPad.Tom1 or MoonNote.EliteDrumPad.Tom2 or MoonNote.EliteDrumPad.Tom3)
            {
                if ((moonNote.flags & MoonNote.Flags.EliteDrums_ChannelFlagRed) != 0)
                {
                    return EliteDrumsChannelFlag.Red;
                }
            }

            if ((moonNote.flags & MoonNote.Flags.EliteDrums_ChannelFlagYellow) != 0)
            {
                return EliteDrumsChannelFlag.Yellow;
            }

            if ((moonNote.flags & MoonNote.Flags.EliteDrums_ChannelFlagBlue) != 0)
            {
                return EliteDrumsChannelFlag.Blue;
            }

            if ((moonNote.flags & MoonNote.Flags.EliteDrums_ChannelFlagGreen) != 0)
            {
                return EliteDrumsChannelFlag.Green;
            }

            return EliteDrumsChannelFlag.None;
        }
    }
}
