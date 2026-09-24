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
        private readonly Dictionary<Instrument, Dictionary<Difficulty, MoonChart>> _downChartsByTarget = new();
        private readonly Dictionary<(Instrument, Difficulty), EliteDrumConversionLedger> _conversionLedgers = new();
        private readonly Dictionary<(Instrument, Difficulty), Dictionary<MoonNote, EliteDrumConversionOrigin>> _emittedOriginsByTargetAndDifficulty = new();

        internal IReadOnlyDictionary<(Instrument, Difficulty), EliteDrumConversionLedger> ConversionLedgers => _conversionLedgers;

        private Dictionary<Difficulty, MoonChart>? DownchartEliteDrumsTrack(InstrumentTrack<EliteDrumNote> eliteDrumsTrack,
            Instrument outputInstrument)
        {
            var downcharts = new Dictionary<Difficulty, MoonChart>()
            {
                {  Difficulty.Easy, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.Easy, outputInstrument) },
                {  Difficulty.Medium, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.Medium, outputInstrument) },
                {  Difficulty.Hard, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.Hard, outputInstrument) },
                {  Difficulty.Expert, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.Expert, outputInstrument) },
                {  Difficulty.ExpertPlus, DownchartEliteDrumsDifficulty(eliteDrumsTrack, Difficulty.ExpertPlus, outputInstrument) },
            };

            var atLeastOneDownchartHasAtLeastOneNote = false;
            YargLogger.LogInfo($"[ED-log] Core converted Elite difficulties: " +
                string.Join(",", downcharts.Select(entry => $"{entry.Key}={entry.Value.notes.Count}")));
            foreach (var downchart in downcharts)
            {
                if (downchart.Value.notes.Count > 0)
                {
                    atLeastOneDownchartHasAtLeastOneNote = true;
                }
            }

            return atLeastOneDownchartHasAtLeastOneNote ? downcharts : null;
        }

        private MoonChart DownchartEliteDrumsDifficulty(InstrumentTrack<EliteDrumNote> eliteDrumsTrack,
            Difficulty difficulty, Instrument outputInstrument)
        {
            MoonChart moonChart = new(MoonChart.GameMode.Drums);
            var droppedOrigins = new List<EliteDrumDroppedOrigin>();

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
            var resolvedConversionOrigins = new Dictionary<MoonNote, EliteDrumConversionOrigin>();
            var notes = ResolveDownchartCollisions(unresolvedChords, resolvedOrigins, resolvedConversionOrigins);
            _emittedOriginsByTargetAndDifficulty[(outputInstrument, difficulty)] = resolvedConversionOrigins;

            // Authored hand-lane phrase instances are recorded here, at conversion time,
            // from the authored Elite track itself. Membership is the set of authored
            // hand-pad gems inside the authored interval; it is never inferred from
            // final pads and never includes nearby ordinary same-pad notes.
            var authoredLanePhrases = BuildAuthoredLanePhraseMemberships(eliteDrumsDifficulty);

            if (eliteDrumsDifficulty.Notes.Count > 0 && notes.Count == 0)
            {
                YargLogger.LogFormatWarning(
                    "Elite Drums difficulty {0} had {1} source notes but converted to zero notes (all source notes were ineligible or discarded).",
                    difficulty, eliteDrumsDifficulty.Notes.Count);
            }

            var codaEndOrigins = new HashSet<EliteDrumNote>();
            var memberships = new List<EliteDrumSourceMembership>();
            foreach (var note in notes)
            {
                if (!resolvedConversionOrigins.TryGetValue(note, out var origin)) continue;
                var membershipEndTick = origin.Source.EndTick > origin.Source.StartTick
                    ? origin.Source.EndTick
                    : origin.Source.StartTick + 1;
                // The membership target is only the ledger's Moon-space grouping key
                // (MoonNote.rawNote); it is never a final output identity. Published
                // descriptors resolve their true final pads from the converted final
                // DrumNote children in EliteDrumVisualDescriptorV1Builder.
                memberships.Add(new(origin, new(outputInstrument, note.rawNote),
                    origin.Source.StartTick, membershipEndTick));
            }
            var emittedOrigins = resolvedConversionOrigins.Values.ToHashSet();
            var dropped = new List<EliteDrumDroppedOrigin>();
            foreach (var sourceChord in eliteDrumsDifficulty.Notes)
            {
                foreach (var source in sourceChord.AllNotes)
                {
                    if (source.SourceDefinition is null) continue;
                    var mainOrigin = new EliteDrumConversionOrigin(source.SourceDefinition);
                    if (!emittedOrigins.Contains(mainOrigin))
                        dropped.Add(new(mainOrigin, source.IsInvisibleTerminator ? "invisible-terminator" : "truncated-or-collision"));
                }
            }
            _conversionLedgers[(outputInstrument, difficulty)] = new(
                EliteDrumFinalPadComponentBuilder.Build(memberships)
                    .Select(component => component), dropped, authoredLanePhrases);
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
                    case PhraseType.EliteDrums_RightCrashLane:
                    case PhraseType.EliteDrums_RideLane:
                    case PhraseType.EliteDrums_Tom3Lane:
                    case PhraseType.EliteDrums_Tom2Lane:
                    case PhraseType.EliteDrums_Tom1Lane:
                    case PhraseType.EliteDrums_LeftCrashLane:
                    case PhraseType.EliteDrums_HiHatLane:
                    case PhraseType.EliteDrums_SnareLane:
                        // Authored Elite hand-lane phrases keep their authored lane
                        // identity and interval through the generated chart instead of
                        // collapsing into native tremolo phrases. DrumsFinalPass validates
                        // them by authored phrase membership (at least three surviving
                        // final hand gems), never by final-pad inference.
                        phrases.Add(new(phrase.Tick, phrase.TickLength, EliteDrumsPhraseToMoonPhrase(phrase.Type)));
                        break;
                    case PhraseType.TremoloLane:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.TremoloLane));
                        break;
                    case PhraseType.TrillLane:
                        phrases.Add(new(phrase.Tick, phrase.TickLength, MoonPhrase.Type.TrillLane));
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

            // Both ordinary and converted phrases must be appended in chronological order.
            // MoonChart.Add intentionally rejects an item earlier than the last item.
            foreach (var phrase in phrases.OrderBy(phrase => phrase.tick))
            {
                moonChart.Add(phrase);
            }

            foreach (var textEvent in textEvents)
            {
                moonChart.Insert(textEvent);
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

        private static MoonPhrase.Type EliteDrumsPhraseToMoonPhrase(PhraseType type) => type switch
        {
            PhraseType.EliteDrums_RightCrashLane => MoonPhrase.Type.EliteDrums_RightCrashLane,
            PhraseType.EliteDrums_RideLane => MoonPhrase.Type.EliteDrums_RideLane,
            PhraseType.EliteDrums_Tom3Lane => MoonPhrase.Type.EliteDrums_Tom3Lane,
            PhraseType.EliteDrums_Tom2Lane => MoonPhrase.Type.EliteDrums_Tom2Lane,
            PhraseType.EliteDrums_Tom1Lane => MoonPhrase.Type.EliteDrums_Tom1Lane,
            PhraseType.EliteDrums_LeftCrashLane => MoonPhrase.Type.EliteDrums_LeftCrashLane,
            PhraseType.EliteDrums_HiHatLane => MoonPhrase.Type.EliteDrums_HiHatLane,
            PhraseType.EliteDrums_SnareLane => MoonPhrase.Type.EliteDrums_SnareLane,
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Not an authored Elite hand-lane phrase type."),
        };

        /// <summary>
        /// Records explicit authored membership for every authored Elite hand-lane phrase
        /// instance: the authored hand-pad gems inside the authored phrase interval. This
        /// runs on the authored source track before any final conversion exists, so the
        /// membership can never be derived from final pads or from nearby ordinary
        /// same-pad notes. Kick and hat-pedal gems are excluded (V1).
        /// </summary>
        private static List<EliteDrumAuthoredLanePhrase> BuildAuthoredLanePhraseMemberships(
            InstrumentDifficulty<EliteDrumNote> eliteDrumsDifficulty)
        {
            var authored = new List<EliteDrumAuthoredLanePhrase>();
            foreach (var phrase in eliteDrumsDifficulty.Phrases)
            {
                if (!EliteDrumAuthoredLanePhraseTypes.IsAuthoredHandLane(phrase.Type)) continue;

                var members = new List<EliteDrumSourceDefinition>();
                var authoredPad = (int) EliteDrumAuthoredLanePhraseTypes.ToAuthoredPad(phrase.Type);
                foreach (var chord in eliteDrumsDifficulty.Notes)
                {
                    foreach (var gem in chord.AllNotes)
                    {
                        var source = gem.SourceDefinition;
                        if (source is null) continue;
                        if (source.Pad != authoredPad) continue;

                        // Half-open authored interval: start included, end excluded, so a
                        // gem on the boundary tick belongs to a touching subsequent phrase.
                        if (source.StartTick < phrase.Tick || source.StartTick >= phrase.TickEnd) continue;
                        members.Add(source);
                    }
                }

                authored.Add(new(phrase.Type, phrase.Tick, phrase.TickEnd, members));
            }

            return authored;
        }

        private static DownchartChord? DownchartEliteDrumsChord(EliteDrumNote eliteDrumChord, List<Phrase> phrases)
        {
            DownchartNote? kick = null;
            DownchartNote? firstHandGem = null;
            DownchartNote? secondHandGem = null;
            foreach (var eliteDrumNote in eliteDrumChord.AllNotes)
            {
                // Disco Flip is applied exactly once by the final target conversion
                // (MoonNoteToFourLane). Do not mutate generated Moon pads here: Pro/Five
                // conversion consumes the generated mix event, while Four Lane preserves
                // the unflipped pad/event semantics.
                var downchartedNotes = DownchartIndividualEliteDrumsNote(eliteDrumNote, false);
                foreach (var downchartedNote in downchartedNotes)
                {
                    if (downchartedNote.MoonNote.drumPad == MoonNote.DrumPad.Kick)
                    {
                        kick = downchartedNote;
                    }
                    else
                    {
                        if (firstHandGem is not null && secondHandGem is not null)
                        {
                            // Keep the normal two-gem output cap for downcharted chords.
                            continue;
                        }

                        if (firstHandGem is null)
                        {
                            firstHandGem = downchartedNote;
                        }
                        else if (secondHandGem is null)
                        {
                            secondHandGem = downchartedNote;
                        }
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

                notes.Add(new(mainNote, eliteDrumNote, 0));

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

                        notes.Add(new(flamPartner, eliteDrumNote, 1));
                    }
                }
            }

            return notes;
        }
        private List<MoonNote> ResolveDownchartCollisions(List<DownchartChord> unresolvedChords,
            Dictionary<MoonNote, EliteDrumNote> resolvedOrigins,
            Dictionary<MoonNote, EliteDrumConversionOrigin> resolvedConversionOrigins)
        {
            List<MoonNote> notes = new();

            foreach (var unresolvedChord in unresolvedChords)
            {
                var resolved = ResolveDownchartCollision(unresolvedChord);
                foreach (var note in resolved)
                {
                    notes.Add(note.MoonNote);
                    resolvedOrigins[note.MoonNote] = note.OriginNote;
                    resolvedConversionOrigins[note.MoonNote] = note.Origin;
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
                if (firstHandGem.OriginNote.ChannelFlag is EliteDrumsChannelFlag.None && secondHandGem.OriginNote.ChannelFlag is EliteDrumsChannelFlag.None)
                {
                    if (firstHandGem.OriginNote.Pad is (int) EliteDrumPad.LeftCrash && secondHandGem.OriginNote.Pad is (int) EliteDrumPad.RightCrash)
                    {
                        firstHandGem.MoonNote.drumPad = MoonNote.DrumPad.Yellow;
                    }
                    else if (firstHandGem.OriginNote.Pad is (int) EliteDrumPad.RightCrash && secondHandGem.OriginNote.Pad is (int) EliteDrumPad.LeftCrash)
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
                    (var leftHandGem, var rightHandGem) = firstHandGem.OriginNote.Pad < secondHandGem.OriginNote.Pad ? (firstHandGem, secondHandGem) : (secondHandGem, firstHandGem);

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
        public DownchartChord(DownchartNote? kick, DownchartNote? firstHandGem,
            DownchartNote? secondHandGem)
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
        public DownchartNote(MoonNote moonNote, EliteDrumNote origin, int expansionOrdinal = 0)
        {
            MoonNote = moonNote;
            OriginNote = origin;
            Origin = origin.SourceDefinition is null
                ? new EliteDrumConversionOrigin(new EliteDrumSourceDefinition(
                    $"legacy:{origin.Tick}:{origin.Pad}", origin.Tick.GetHashCode(), origin.Pad,
                    origin.Tick, origin.TickLength, origin.Time, origin.TimeLength), expansionOrdinal)
                : new EliteDrumConversionOrigin(origin.SourceDefinition, expansionOrdinal);
        }

        public MoonNote MoonNote { get; }
        public EliteDrumNote OriginNote { get; }
        public EliteDrumConversionOrigin Origin { get; }

    }
}
