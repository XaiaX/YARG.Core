using System;
using System.Collections.Generic;
using System.Linq;
using MoonscraperChartEditor.Song;
using MoonscraperChartEditor.Song.IO;
using YARG.Core.Parsing;

namespace YARG.Core.Chart
{
    internal partial class MoonSongLoader
    {
        // A fresh loader keeps conversion context and generated provenance player-local.
        internal static InstrumentDifficulty<DrumNote> ConvertPlaybackClassic(
            InstrumentDifficulty<DrumNote> source, Instrument output, bool beginner, uint resolution)
        {
            var settings = ParseSettings.Default;
            settings.DrumsType = source.Instrument == Instrument.FiveLaneDrums ? DrumsType.FiveLane : DrumsType.FourLane;
            var song = new MoonSong(resolution);
            var moon = song.GetChart(MoonSong.MoonInstrument.Drums, YargDifficultyToMoonDifficulty(source.Difficulty));
            var originalMembers = new Dictionary<MoonNote, DrumNote>();
            foreach (var chord in source.Notes)
            {
                foreach (var note in chord.AllNotes)
                {
                    MoonNote.DrumPad pad;
                    var flags = MoonNote.Flags.None;
                    if (source.Instrument == Instrument.FiveLaneDrums)
                    {
                        pad = (FiveLaneDrumPad)note.Pad switch
                        {
                            FiveLaneDrumPad.Kick => MoonNote.DrumPad.Kick,
                            FiveLaneDrumPad.Red => MoonNote.DrumPad.Red,
                            FiveLaneDrumPad.Yellow => MoonNote.DrumPad.Yellow,
                            FiveLaneDrumPad.Blue => MoonNote.DrumPad.Blue,
                            FiveLaneDrumPad.Orange => MoonNote.DrumPad.Orange,
                            FiveLaneDrumPad.Green => MoonNote.DrumPad.Green,
                            _ => throw new ArgumentException("Not a native Five-Lane pad")
                        };
                    }
                    else
                    {
                        pad = (FourLaneDrumPad)note.Pad switch
                        {
                            FourLaneDrumPad.Kick => MoonNote.DrumPad.Kick,
                            FourLaneDrumPad.RedDrum => MoonNote.DrumPad.Red,
                            FourLaneDrumPad.YellowDrum or FourLaneDrumPad.YellowCymbal => MoonNote.DrumPad.Yellow,
                            FourLaneDrumPad.BlueDrum or FourLaneDrumPad.BlueCymbal => MoonNote.DrumPad.Blue,
                            FourLaneDrumPad.GreenDrum or FourLaneDrumPad.GreenCymbal => MoonNote.DrumPad.Green,
                            _ => throw new ArgumentException("Not a native Four-Lane pad")
                        };
                        if (note.Pad >= (int)FourLaneDrumPad.YellowCymbal) flags |= MoonNote.Flags.ProDrums_Cymbal;
                    }
                    if (note.IsDoubleKick) flags |= MoonNote.Flags.InstrumentPlus;
                    if (note.IsAccent) flags |= MoonNote.Flags.ProDrums_Accent;
                    if (note.IsGhost) flags |= MoonNote.Flags.ProDrums_Ghost;
                    var emitted = new MoonNote(note.Tick, (int)pad, 0, flags);
                    moon.Add(emitted);
                    originalMembers.Add(emitted, note);
                }
            }
            var loader = new MoonSongLoader(song, settings) { _loadingAuthoredDrums = true };
            CreateNoteDelegate<DrumNote> create = output == Instrument.FiveLaneDrums ? loader.CreateFiveLaneDrumNote : loader.CreateFourLaneDrumNote;
            if (beginner) create = output == Instrument.FiveLaneDrums ? loader.CreateFiveLaneDrumBeginnerNote : loader.CreateFourLaneDrumBeginnerNote;
            DrumNote CreateWithMembership(MoonNote moonNote, Dictionary<MoonPhrase.Type, MoonPhrase> phrases, List<DrumNote> notes)
            {
                var convertedNote = create(moonNote, phrases, notes);
                var original = originalMembers[moonNote];
                convertedNote.ActivateFlag(original.Flags & (NoteFlags.CodaStart | NoteFlags.CodaEnd));
                if (original.IsTremolo) convertedNote.ActivateFlag(NoteFlags.Tremolo);
                if (original.IsTrill) convertedNote.ActivateFlag(beginner ? NoteFlags.Tremolo : NoteFlags.Trill);
                if (original.IsKickLane && !beginner) convertedNote.ActivateFlag(DrumNoteFlags.KickLane);
                return convertedNote;
            }
            var converted = loader.LoadDrumDifficulty(output, beginner ? Difficulty.Beginner : source.Difficulty,
                CreateWithMembership, loader.HandleTextEvent, DrumsFinalPass);
            converted.Phrases.AddRange(source.Phrases.Select(p => p.Clone()));
            converted.TextEvents.AddRange(source.TextEvents.Select(p => p.Clone()));
            converted.RangeShiftEvents.AddRange(source.RangeShiftEvents.Select(p => p.Clone()));
            return converted;
        }

        internal static InstrumentDifficulty<DrumNote> ConvertPlaybackElite(
            InstrumentDifficulty<EliteDrumNote> source, Instrument output, uint resolution)
        {
            var settings = ParseSettings.Default;
            settings.DrumsType = DrumsType.FourLane;
            var loader = new MoonSongLoader(new MoonSong(resolution), settings) { _loadingAuthoredDrums = true };
            // The existing downcharter expects all historical tier keys; populate them with empty
            // difficulties, never a different source's notes.
            var tiers = new Dictionary<Difficulty, InstrumentDifficulty<EliteDrumNote>>();
            foreach (var tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard, Difficulty.Expert, Difficulty.ExpertPlus })
                tiers[tier] = tier == source.Difficulty ? source : new(Instrument.EliteDrums, tier);
            var track = new InstrumentTrack<EliteDrumNote>(Instrument.EliteDrums, tiers);
            return loader.LoadEliteDrumsDownchartTrack(output, track).GetDifficulty(source.Difficulty);
        }
    }
}
