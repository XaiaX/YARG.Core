using MoonscraperChartEditor.Song;
using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Logging;
using static YARG.Core.Chart.EliteDrumNote;

namespace YARG.Core.Chart
{
    internal partial class MoonSongLoader : ISongLoader
    {
        private Dictionary<Difficulty, MoonChart>? _downCharts = null;

        private List<MoonText> _downchartTextEvents = new();

        private Dictionary<Difficulty, MoonChart>? DownchartEliteDrumsTrack(InstrumentTrack<EliteDrumNote> eliteDrumsTrack)
        {
            var downcharts = new Dictionary<Difficulty, MoonChart>()
            {
                {  Difficulty.Easy, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.Easy) },
                {  Difficulty.Medium, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.Medium) },
                {  Difficulty.Hard, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.Hard) },
                {  Difficulty.Expert, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.Expert) },
                {  Difficulty.ExpertPlus, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.ExpertPlus) },
            };

            var atLeastOneDownchartHasAtLeastOneNote = false;
            YargLogger.LogInfo($"[ED-log] Core converted Elite difficulties: " +
                string.Join(",", downcharts.Select(entry => $"{entry.Key}={entry.Value.notes.Count}")));
            foreach (var downchart in downcharts)
            {
                if (downchart.Value.notes.Count == 0)
                {
                    continue;
                }

                atLeastOneDownchartHasAtLeastOneNote = true;

                foreach (var textEvent in _downchartTextEvents)
                {
                    downchart.Value.Insert(textEvent);
                }
            }

            return atLeastOneDownchartHasAtLeastOneNote ? downcharts : null;
        }

        private MoonChart DownchartEliteDrumsDifficulty(InstrumentTrack<EliteDrumNote> eliteDrumsTrack, Difficulty difficulty)
        {
            MoonChart moonChart = new(MoonChart.GameMode.Drums);

            var eliteDrumsDifficulty = eliteDrumsTrack.GetDifficulty(difficulty);

            // Downchart notes
            List<DownchartChord> unresolvedChords = new();
            foreach (var eliteDrumNote in eliteDrumsDifficulty.Notes)
            {
                var chord = DownchartEliteDrumsChord(eliteDrumNote, eliteDrumsDifficulty.Phrases);
                if (chord is not null)
                {
                    unresolvedChords.Add(chord.Value);
                }
            }

            var resolvedOrigins = new Dictionary<MoonNote, EliteDrumNote>();
            var notes = ResolveDownchartCollisions(unresolvedChords, resolvedOrigins);

            if (eliteDrumsDifficulty.Notes.Count > 0 && notes.Count == 0)
            {
                YargLogger.LogFormatWarning(
                    "Elite Drums difficulty {0} had {1} source notes but converted to zero notes (all source notes were ineligible or discarded).",
                    difficulty, eliteDrumsDifficulty.Notes.Count);
            }

            var codaEndOrigins = new HashSet<EliteDrumNote>();
            for (var i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                if (resolvedOrigins.TryGetValue(note, out var origin) && origin.IsCodaEnd)
                {
                    note.flags |= MoonNote.Flags.CodaEnd;
                    codaEndOrigins.Add(origin);
                }
                if (i > 0)
                {
                    note.previous = notes[i - 1];
                    notes[i - 1].next = note;
                }

                moonChart.Add(note);
            }

            // A CodaEnd source can be omitted (for example an unforced pedal or a
            // capped third hand gem). Retain the marker on the final surviving output
            // chord at or before that source endpoint, without touching the source track.
            foreach (var sourceChord in eliteDrumsDifficulty.Notes)
            {
                foreach (var source in sourceChord.AllNotes)
                {
                    if (!source.IsCodaEnd || codaEndOrigins.Contains(source))
                    {
                        continue;
                    }

                    var fallback = notes.LastOrDefault(note => note.tick <= source.Tick);
                    if (fallback is not null)
                    {
                        fallback.flags |= MoonNote.Flags.CodaEnd;
                    }
                }
            }

            var (discoOnText, discoOffText) = GetDiscoFlipEventText(difficulty);
            List<MoonPhrase> phrases = new();
            List<MoonText> textEvents = new()
            {
                new(discoOffText, 0)
            };

            foreach (var phrase in eliteDrumsDifficulty.Phrases)
            {
                switch (phrase.Type)
                {
                    case PhraseType.StarPower:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.Starpower));
                        break;
                    case PhraseType.DrumFill:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.ProDrums_Activation));
                        break;
                    case PhraseType.VersusPlayer1:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.Versus_Player1));
                        break;
                    case PhraseType.VersusPlayer2:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.Versus_Player2));
                        break;
                    case PhraseType.EliteDrums_KickLane:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.ProDrums_KickLane));
                        break;
                    case PhraseType.BigRockEnding:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.BigRockEnding));
                        break;
                    case PhraseType.Coda:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.Coda));
                        break;
                    case PhraseType.Solo:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.Solo));
                        break;
                    case PhraseType.EliteDrums_DiscoFlip:
                        if (phrase.Tick == 0)
                        {
                            textEvents[0].text = discoOnText;
                        }
                        else
                        {
                            textEvents.Add(new(discoOnText, phrase.Tick));
                        }
                        textEvents.Add(new(discoOffText, phrase.TickEnd));
                        break;
                }
            }

            phrases.AddRange(AddConvertedEliteLanePhrases(eliteDrumsDifficulty.Phrases, notes, resolvedOrigins));

            // Both ordinary and converted phrases must be appended in chronological order.
            // MoonChart.Add intentionally rejects an item earlier than the last item.
            foreach (var phrase in phrases.OrderBy(phrase => phrase.tick))
            {
                moonChart.Add(phrase);
            }

            foreach (var textEvent in textEvents)
            {
                _downchartTextEvents.Add(textEvent);
            }

            return moonChart;
        }

        private (string onText, string offText) GetDiscoFlipEventText(Difficulty difficulty)
        {
            var diffNum = difficulty switch
            {
                Difficulty.Beginner or Difficulty.Easy => 0,
                Difficulty.Medium => 1,
                Difficulty.Hard => 2,
                Difficulty.Expert or Difficulty.ExpertPlus => 3,
                _ => throw new Exception("Unreachable")
            };

            return ($"mix {diffNum} drums0d", $"mix {diffNum} drums0");
        }

        private static DownchartChord? DownchartEliteDrumsChord(EliteDrumNote eliteDrumChord, List<Phrase> phrases)
        {
            DownchartNote? kick = null;
            DownchartNote? firstHandGem = null;
            DownchartNote? secondHandGem = null;

            var chordIsInDiscoFlip = false;
            foreach (var phrase in phrases)
            {
                if (phrase.Tick > eliteDrumChord.Tick)
                {
                    break;
                }
                if (phrase.Type is PhraseType.EliteDrums_DiscoFlip)
                {
                    if (phrase.Tick <= eliteDrumChord.Tick && phrase.TickEnd > eliteDrumChord.Tick)
                    {
                        chordIsInDiscoFlip = true;
                        break;
                    }
                }
            }

            foreach (var eliteDrumNote in eliteDrumChord.AllNotes)
            {
                var downchartedNotes = DownchartIndividualEliteDrumsNote(eliteDrumNote, chordIsInDiscoFlip);
                foreach (var downchartedNote in downchartedNotes)
                {
                    if (downchartedNote.MoonNote.drumPad == MoonNote.DrumPad.Kick)
                    {
                        kick = downchartedNote;
                    }
                    else if (firstHandGem is null)
                    {
                        firstHandGem = downchartedNote;
                    }
                    else if (secondHandGem is null)
                    {
                        secondHandGem = downchartedNote;
                    }
                }
            }

            if (kick is null && firstHandGem is null)
            {
                // Downcharted to nothing; must have been just an unforced Hat Pedal note
                return null;
            }

            return new(kick, firstHandGem, secondHandGem);
        }

        // In most cases, returns 1 note. Unforced or invisible hat pedals return 0 notes, while flams return 2.
        private static List<DownchartNote> DownchartIndividualEliteDrumsNote(EliteDrumNote eliteDrumNote, bool noteIsInDiscoFlip)
        {
            List<DownchartNote> notes = new();

            // Never downchart invisible terminator hat pedals, even if channel flagged
            if (eliteDrumNote.Pad is (int)EliteDrumPad.HatPedal && eliteDrumNote.IsInvisibleTerminator) return notes;

            (MoonNote.DrumPad? pad, MoonNote.Flags flags) = ((EliteDrumPad) eliteDrumNote.Pad) switch
            {
                EliteDrumPad.HatPedal =>    (GetDrumPadForChannelFlag(eliteDrumNote,   null),                      MoonNote.Flags.ProDrums_Cymbal),
                EliteDrumPad.Kick =>        (                                          MoonNote.DrumPad.Kick,      eliteDrumNote.IsDoubleKick ? MoonNote.Flags.InstrumentPlus : MoonNote.Flags.None),
                EliteDrumPad.Snare =>       (GetDrumPadForChannelFlag(eliteDrumNote,   MoonNote.DrumPad.Red),      MoonNote.Flags.None),
                EliteDrumPad.HiHat =>       (GetDrumPadForChannelFlag(eliteDrumNote,   MoonNote.DrumPad.Yellow),   MoonNote.Flags.ProDrums_Cymbal),
                EliteDrumPad.LeftCrash =>   (GetDrumPadForChannelFlag(eliteDrumNote,   MoonNote.DrumPad.Blue),     MoonNote.Flags.ProDrums_Cymbal),
                EliteDrumPad.Tom1 =>        (GetDrumPadForChannelFlag(eliteDrumNote,   MoonNote.DrumPad.Yellow),   MoonNote.Flags.None),
                EliteDrumPad.Tom2 =>        (GetDrumPadForChannelFlag(eliteDrumNote,   MoonNote.DrumPad.Blue),     MoonNote.Flags.None),
                EliteDrumPad.Tom3 =>        (GetDrumPadForChannelFlag(eliteDrumNote,   MoonNote.DrumPad.Green),    MoonNote.Flags.None),
                EliteDrumPad.Ride =>        (GetDrumPadForChannelFlag(eliteDrumNote,   MoonNote.DrumPad.Blue),     MoonNote.Flags.ProDrums_Cymbal),
                EliteDrumPad.RightCrash =>  (GetDrumPadForChannelFlag(eliteDrumNote,   MoonNote.DrumPad.Green),    MoonNote.Flags.ProDrums_Cymbal),
                _ => throw new Exception("Unreachable.")
            };

            if (pad is not null)
            {
                if (noteIsInDiscoFlip)
                {
                    // Disco-flipped snares are charted as Ycyms
                    if (pad is MoonNote.DrumPad.Red)
                    {
                        pad = MoonNote.DrumPad.Yellow;
                        flags &= MoonNote.Flags.ProDrums_Cymbal;
                    }
                    else if (pad is MoonNote.DrumPad.Yellow)
                    {
                        // Disco flipped Ycyms are charted as snares
                        if ((flags & MoonNote.Flags.ProDrums_Cymbal) != 0)
                        {
                            pad = MoonNote.DrumPad.Red;
                            flags &= ~MoonNote.Flags.ProDrums_Cymbal;
                        }

                        // Disco flipped Ytoms are treated like collisions, shunted to Btoms
                        else
                        {
                            pad = MoonNote.DrumPad.Blue;
                        }
                    }
                }

                flags |= eliteDrumNote.Dynamics switch
                {
                    DrumNoteType.Accent => MoonNote.Flags.ProDrums_Accent,
                    DrumNoteType.Ghost => MoonNote.Flags.ProDrums_Ghost,
                    _ => MoonNote.Flags.None
                };

                var mainNote = new MoonNote(eliteDrumNote.Tick, -1, 0, flags)
                {
                    drumPad = pad.Value
                };

                notes.Add(new(mainNote, eliteDrumNote));

                if (eliteDrumNote.IsFlam)
                {
                    MoonNote.DrumPad? otherPad = pad.Value switch
                    {
                        MoonNote.DrumPad.Kick => null,
                        MoonNote.DrumPad.Red => MoonNote.DrumPad.Yellow,
                        MoonNote.DrumPad.Yellow => MoonNote.DrumPad.Blue,
                        MoonNote.DrumPad.Blue => MoonNote.DrumPad.Green,
                        MoonNote.DrumPad.Green => MoonNote.DrumPad.Blue,
                        _ => throw new Exception("Unreachable.")
                    };

                    if (otherPad is not null)
                    {
                        var flamPartner = new MoonNote(eliteDrumNote.Tick, -1, 0, flags)
                        {
                            drumPad = otherPad.Value
                        };

                        notes.Add(new(flamPartner, eliteDrumNote));
                    }
                }
            }

            return notes;
        }
        private List<MoonNote> ResolveDownchartCollisions(List<DownchartChord> unresolvedChords,
            Dictionary<MoonNote, EliteDrumNote> resolvedOrigins)
        {
            List<MoonNote> notes = new();

            foreach (var unresolvedChord in unresolvedChords)
            {
                foreach (var note in ResolveDownchartCollision(unresolvedChord))
                {
                    notes.Add(note.MoonNote);
                    resolvedOrigins[note.MoonNote] = note.Origin;
                }
            }

            return notes;
        }

        private List<DownchartNote> ResolveDownchartCollision(DownchartChord downchartChord)
        {
            List<DownchartNote> notes = new();

            if (downchartChord.Kick is not null)
            {
                notes.Add(downchartChord.Kick.Value);
            }

            if (downchartChord.SecondHandGem is null)
            {
                // Can't have collisions without a second hand gem, so return early
                if (downchartChord.FirstHandGem is not null)
                {
                    notes.Add(downchartChord.FirstHandGem.Value);
                }

                return notes;
            }

            var firstHandGem = downchartChord.FirstHandGem!.Value;
            var secondHandGem = downchartChord.SecondHandGem!.Value;


            if (firstHandGem.MoonNote.drumPad != secondHandGem.MoonNote.drumPad)
            {
                // Two hand gems, but no collision

                // Special case: Unforced LCrash + Unforced RCrash resolves to YG instead of BG
                if (firstHandGem.Origin.ChannelFlag is EliteDrumsChannelFlag.None && secondHandGem.Origin.ChannelFlag is EliteDrumsChannelFlag.None)
                {
                    if (firstHandGem.Origin.Pad is (int) EliteDrumPad.LeftCrash && secondHandGem.Origin.Pad is (int) EliteDrumPad.RightCrash)
                    {
                        firstHandGem.MoonNote.drumPad = MoonNote.DrumPad.Yellow;
                    }
                    else if (firstHandGem.Origin.Pad is (int) EliteDrumPad.RightCrash && secondHandGem.Origin.Pad is (int) EliteDrumPad.LeftCrash)
                    {
                        secondHandGem.MoonNote.drumPad = MoonNote.DrumPad.Yellow;
                    }
                }
            }
            else
            {

                // Two hand gems with equal colors - collision!

                // For tom/cymbal collisions, the tom goes on the left. For tom/tom and cym/cym collisions, preserve the
                // handedness of the dynamics from the original Elite Drums chord
                MoonNote.DrumPad newLeftPad;
                MoonNote.DrumPad newRightPad;

                var firstHandGemIsCymbal = (firstHandGem.MoonNote.flags & MoonNote.Flags.ProDrums_Cymbal) != 0;
                var secondHandGemIsCymbal = (secondHandGem.MoonNote.flags & MoonNote.Flags.ProDrums_Cymbal) != 0;

                if (firstHandGemIsCymbal == secondHandGemIsCymbal)
                {
                    // This is a tom/tom or cym/cym collision, so we'll need to preserve the handedness of the dynamics
                    // from the original Elite Drums chord
                    (var leftHandGem, var rightHandGem) = firstHandGem.Origin.Pad < secondHandGem.Origin.Pad ? (firstHandGem, secondHandGem) : (secondHandGem, firstHandGem);

                    (newLeftPad, newRightPad) = firstHandGem.MoonNote.drumPad switch
                    {
                        MoonNote.DrumPad.Red => (MoonNote.DrumPad.Red, MoonNote.DrumPad.Yellow),
                        MoonNote.DrumPad.Yellow => (MoonNote.DrumPad.Yellow, MoonNote.DrumPad.Blue),
                        MoonNote.DrumPad.Blue => (MoonNote.DrumPad.Blue, MoonNote.DrumPad.Green),
                        MoonNote.DrumPad.Green => (MoonNote.DrumPad.Blue, MoonNote.DrumPad.Green),
                        _ => throw new Exception("Unreachable.")
                    };

                    leftHandGem.MoonNote.drumPad = newLeftPad;
                    rightHandGem.MoonNote.drumPad = newRightPad;
                }
                else
                {
                    // This is a tom/cym collision (or vice-versa)
                    // Nudge the tom to the left
                    (var cym, var tom) = firstHandGemIsCymbal ? (firstHandGem, secondHandGem) : (secondHandGem, firstHandGem);

                    switch (cym.MoonNote.drumPad)
                    {
                        case MoonNote.DrumPad.Yellow:
                            newLeftPad = MoonNote.DrumPad.Red;
                            newRightPad = MoonNote.DrumPad.Yellow;
                            break;
                        case MoonNote.DrumPad.Blue:
                            newLeftPad = MoonNote.DrumPad.Yellow;
                            newRightPad = MoonNote.DrumPad.Blue;
                            break;
                        case MoonNote.DrumPad.Green:
                            newLeftPad = MoonNote.DrumPad.Blue;
                            newRightPad = MoonNote.DrumPad.Green;
                            break;
                        default:
                            throw new Exception("Unreachable.");
                    }

                    tom.MoonNote.drumPad = newLeftPad;
                    cym.MoonNote.drumPad = newRightPad;
                }
            }

            notes.Add(firstHandGem);
            notes.Add(secondHandGem);
            return notes;
        }
        private static List<MoonPhrase> AddConvertedEliteLanePhrases(List<Phrase> sourcePhrases,
            List<MoonNote> notes, Dictionary<MoonNote, EliteDrumNote> origins)
        {
            List<MoonPhrase> convertedPhrases = new();
            foreach (var phrase in sourcePhrases)
            {
                if (phrase.Type is not (PhraseType.EliteDrums_KickLane or
                    PhraseType.EliteDrums_RightCrashLane or PhraseType.EliteDrums_RideLane or
                    PhraseType.EliteDrums_Tom3Lane or PhraseType.EliteDrums_Tom2Lane or
                    PhraseType.EliteDrums_Tom1Lane or PhraseType.EliteDrums_LeftCrashLane or
                    PhraseType.EliteDrums_HiHatLane or PhraseType.EliteDrums_SnareLane or
                    PhraseType.EliteDrums_HatPedalLane))
                {
                    continue;
                }

                var laneNotes = notes.Where(note => note.tick >= phrase.Tick &&
                    note.tick <= phrase.TickEnd && origins.TryGetValue(note, out var origin) &&
                    origin.Pad == GetElitePadForPhrase(phrase.Type)).ToList();
                if (laneNotes.Count == 0)
                {
                    continue;
                }

                var distinctSources = laneNotes.Select(note => origins[note]).Distinct().ToList();
                if (distinctSources.Count < 2)
                {
                    continue;
                }

                var firstChord = laneNotes.Where(note => note.tick == laneNotes[0].tick).ToList();
                var firstOutput = phrase.Type == PhraseType.EliteDrums_KickLane
                    ? notes.FirstOrDefault(note => note.tick >= phrase.Tick &&
                        note.tick <= phrase.TickEnd && note.drumPad == MoonNote.DrumPad.Kick)
                    : notes.FirstOrDefault(note => note.tick >= phrase.Tick &&
                        note.tick <= phrase.TickEnd && note.drumPad != MoonNote.DrumPad.Kick);
                var hasRepresentableTarget = phrase.Type == PhraseType.EliteDrums_KickLane
                    ? firstChord.Any(note => note.drumPad == MoonNote.DrumPad.Kick) && firstOutput is not null
                    : firstChord.Any(note => note.drumPad != MoonNote.DrumPad.Kick &&
                        laneNotes.Skip(firstChord.Count).Any(later => later.drumPad == note.drumPad &&
                            origins[later] != origins[note])) && firstOutput is not null &&
                        firstOutput.drumPad == firstChord.First(note => note.drumPad != MoonNote.DrumPad.Kick).drumPad;
                if (!hasRepresentableTarget)
                {
                    continue;
                }

                var type = phrase.Type == PhraseType.EliteDrums_KickLane
                    ? MoonPhrase.Type.ProDrums_KickLane : MoonPhrase.Type.TremoloLane;
                if (!convertedPhrases.Any(existing => existing.tick == phrase.Tick &&
                    existing.length == phrase.TickLength && existing.type == type))
                {
                    convertedPhrases.Add(new MoonPhrase(phrase.Tick, phrase.TickLength, type));
                }
            }

            return convertedPhrases;
        }

        private static int GetElitePadForPhrase(PhraseType type) => type switch
        {
            PhraseType.EliteDrums_HatPedalLane => (int) EliteDrumPad.HatPedal,
            PhraseType.EliteDrums_KickLane => (int) EliteDrumPad.Kick,
            PhraseType.EliteDrums_SnareLane => (int) EliteDrumPad.Snare,
            PhraseType.EliteDrums_HiHatLane => (int) EliteDrumPad.HiHat,
            PhraseType.EliteDrums_LeftCrashLane => (int) EliteDrumPad.LeftCrash,
            PhraseType.EliteDrums_Tom1Lane => (int) EliteDrumPad.Tom1,
            PhraseType.EliteDrums_Tom2Lane => (int) EliteDrumPad.Tom2,
            PhraseType.EliteDrums_Tom3Lane => (int) EliteDrumPad.Tom3,
            PhraseType.EliteDrums_RideLane => (int) EliteDrumPad.Ride,
            PhraseType.EliteDrums_RightCrashLane => (int) EliteDrumPad.RightCrash,
            _ => -1,
        };

        private static MoonNote.DrumPad? GetDrumPadForChannelFlag(EliteDrumNote drum, MoonNote.DrumPad? unforced)
        {
            return drum.ChannelFlag switch
            {
                EliteDrumsChannelFlag.Red => MoonNote.DrumPad.Red,
                EliteDrumsChannelFlag.Yellow => MoonNote.DrumPad.Yellow,
                EliteDrumsChannelFlag.Blue => MoonNote.DrumPad.Blue,
                EliteDrumsChannelFlag.Green => MoonNote.DrumPad.Green,
                _ => unforced
            };
        }
    }

    internal readonly struct DownchartChord
    {
        public DownchartChord(DownchartNote? kick, DownchartNote? firstHandGem, DownchartNote? secondHandGem)
        {
            Kick = kick;
            FirstHandGem = firstHandGem;
            SecondHandGem = secondHandGem;
        }

        public DownchartNote? Kick { get; }
        public DownchartNote? FirstHandGem { get; }
        public DownchartNote? SecondHandGem { get; }

    }

    internal readonly struct DownchartNote {
        public DownchartNote(MoonNote moonNote, EliteDrumNote origin)
        {
            MoonNote = moonNote;
            Origin = origin;
        }

        public MoonNote MoonNote { get; }
        public EliteDrumNote Origin { get; }

    }
}
