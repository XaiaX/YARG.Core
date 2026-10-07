using System.Collections.Generic;
using MoonscraperChartEditor.Song;
using YARG.Core.Game;

namespace YARG.Core.Chart
{
    internal partial class MoonSongLoader
    {
        public AuthoredDrumSourceCollection LoadAuthoredDrumSources()
        {
            var sources = new AuthoredDrumSourceCollection();
            _loadingAuthoredDrums = true;
            try
            {
                for (var tier = Difficulty.Easy; tier <= Difficulty.Expert; tier++)
                {
                    // FourLane and Pro share one authored parser domain. Never infer a source
                    // from a public track: those tracks may be converted or generated.
                    bool fiveLane = _settings.DrumsType == DrumsType.FiveLane;
                    var format = fiveLane ? DrumSourceFormat.FiveLane : DrumSourceFormat.FourLane;
                    var instrument = fiveLane ? Instrument.FiveLaneDrums : Instrument.ProDrums;
                    var moon = GetMoonChart(instrument, tier);
                    if (moon.notes.Count > 0)
                    {
                        var facts = GetAuthoredDrumFacts(format, tier, moon, out var paired);
                        CreateNoteDelegate<DrumNote> create = fiveLane ? CreateFiveLaneDrumNote : CreateFourLaneDrumNote;
                        var notes = LoadDrumDifficulty(instrument, tier, create, HandleTextEvent, DrumsFinalPass);
                        sources.Add(new AuthoredDrumSourceTier(facts, notes, null, paired));
                    }

                    moon = GetMoonChart(Instrument.EliteDrums, tier);
                    if (moon.notes.Count > 0)
                    {
                        var facts = GetAuthoredDrumFacts(DrumSourceFormat.Elite, tier, moon, out var paired);
                        _eliteSourceOrdinal = 0;
                        var notes = LoadNativeEliteDrumsDifficulty(Instrument.EliteDrums, tier, CreateEliteDrumNote);
                        sources.Add(new AuthoredDrumSourceTier(facts, null, notes, paired));
                    }
                }
            }
            finally
            {
                _loadingAuthoredDrums = false;
                ResetDrumMixState();
            }
            return sources;
        }

        private static DrumSourceTierFacts GetAuthoredDrumFacts(DrumSourceFormat source, Difficulty tier,
            MoonChart chart, out List<uint> paired)
        {
            bool ordinary = false, extra = false, other = false, classicEligible = false;
            paired = new List<uint>();
            foreach (var note in chart.notes)
            {
                // Invisible Elite hat-pedal terminators are control notes, not playable facts.
                // Keep them in the loaded source, but do not make an otherwise empty tier eligible.
                if (source == DrumSourceFormat.Elite &&
                    note.eliteDrumPad == MoonNote.EliteDrumPad.HatPedal &&
                    (note.flags & MoonNote.Flags.EliteDrums_InvisibleTerminator) != 0)
                    continue;

                bool kick = source == DrumSourceFormat.Elite
                    ? note.eliteDrumPad == MoonNote.EliteDrumPad.Kick
                    : note.drumPad == MoonNote.DrumPad.Kick;
                if (kick)
                {
                    if ((note.flags & MoonNote.Flags.InstrumentPlus) != 0) extra = true;
                    else ordinary = true;
                    if (note.pairedExtraKick is not null) paired.Add(note.tick);
                    classicEligible = true;
                }
                else
                {
                    other = true;
                    // Hat-pedal-only Elite tiers do not have playable classic output.
                    if (source != DrumSourceFormat.Elite || note.eliteDrumPad != MoonNote.EliteDrumPad.HatPedal)
                        classicEligible = true;
                }
            }
            return new DrumSourceTierFacts(source, tier, ordinary, extra, other, paired.Count > 0, classicEligible);
        }
    }
}
