using System;
using System.Collections.Generic;
using System.Text;
using MoonscraperChartEditor.Song.IO;
using YARG.Core.IO;

namespace YARG.Core.Song
{
    internal static class Midi_EliteDrums_Preparser
    {
        private const int CYMBAL_CHANNEL_FLAG_Y = 11;
        private const int CYMBAL_CHANNEL_FLAG_B = 12;
        private const int CYMBAL_CHANNEL_FLAG_G = 13;


        private const int DOUBLE_KICK_NOTE = 73;
        private const int NUM_LANES = 11;
        private const int ELITE_MAX = 82;
        const int DOUBLE_KICK_MASK = 1 << (3 * NUM_LANES + 1);

        // Lane indices (note value % 24) of the pads the chord-aware hat pedal
        // eligibility rule cares about
        private const int HAT_PEDAL_LANE = 0;
        private const int HIHAT_LANE = 4;

        // "Indifferent hat" marker notes (ELITE_DRUMS_DIFF_START_LOOKUP[diff] + 14).
        // The full reader toggles MoonNote.Flags.EliteDrums_ForcedIndifferent on the
        // hi-hat notes of EVERY difficulty whose tick lies inside the marker's
        // note-on/note-off range, so all four values are equivalent range markers.
        private const int INDIFFERENT_HAT_MARKER_E = 16;
        private const int INDIFFERENT_HAT_MARKER_M = 40;
        private const int INDIFFERENT_HAT_MARKER_H = 64;
        private const int INDIFFERENT_HAT_MARKER_X = 88;

        private static readonly byte[] STRICT_HAT_PEDAL_STATE_TEXT =
            Encoding.ASCII.GetBytes(MidIOHelper.STRICT_HAT_PEDAL_STATE);

        // Returns a separate DifficultyMask for the downchart because an Elite Drums chart that is entirely
        // unflagged stomps and/or splashes does not produce a playable downchart, so there might be fewer
        // downchart difficulties than Elite Drums difficulties.
        public static unsafe (DifficultyMask eliteDrumsDifficulties, DifficultyMask downchartDifficulties) Parse(YARGMidiTrack track)
        {
            var validations = default(DifficultyMask);
            var downchartValidations = default(DifficultyMask);

            long statusBitMask = 0;
            long downchartStatusBitMask = 0;

            // Chord context for the tick currently being scanned. A hat pedal's
            // downchart eligibility cannot be decided from the note-on alone: the
            // full reader's SuppressNonStrictStompsAndSplashes drops a non-strict
            // hat pedal that is chorded with a hi-hat lacking the
            // forced-indifferent flag (an "invisible terminator"), and
            // MoonSongLoader's downchart builder never converts those, even when
            // channel flagged. This mask must therefore agree with the loader's
            // chord-aware notion, not just with note presence.
            var strictHatPedalState = false;
            var currentTick = -1L;

            // Bit per difficulty-block marker value (16/40/64/88): set while its
            // note-on/note-off range is open. The reader ignores duplicate marker
            // note-ons while one is open, so a set bit mirrors that exactly
            var openIndifferentMarkerValues = 0;

            // Per-tick chord state, all indexed by difficulty (bit 0..3)
            var tickHatPedals = 0;         // a hat pedal note-on was seen at this tick
            var tickEligibleHatPedals = 0; // of those, channel flagged and not ghost velocity
            var tickStrictHatPedals = 0;   // of those, parsed after the strict-state text event
            var tickHiHats = 0;            // a hi-hat note-on was seen at this tick
            var tickDeferredPedalOffs = 0; // hat pedal note-offs at this tick awaiting the eligibility decision

            void FinalizeTick()
            {
                // Hi-hats at this tick are forced-indifferent when an odd number of
                // marker ranges cover the tick (the reader toggles the flag per range)
                var coveringMarkers = 0;
                for (var block = 0; block < 4; block++)
                {
                    if ((openIndifferentMarkerValues & (1 << block)) != 0)
                    {
                        coveringMarkers++;
                    }
                }

                // Non-strict pedals chorded with a non-indifferent hi-hat are
                // suppressed into invisible terminators by the full reader
                var suppressed = 0;
                if ((coveringMarkers & 1) == 0)
                {
                    suppressed = tickEligibleHatPedals & ~tickStrictHatPedals & tickHiHats;
                }

                var contributes = tickEligibleHatPedals & ~suppressed;
                for (var diffIndex = 0; diffIndex < 4; diffIndex++)
                {
                    if ((contributes & (1 << diffIndex)) == 0)
                    {
                        continue;
                    }

                    downchartStatusBitMask |= 1L << (diffIndex * NUM_LANES + HAT_PEDAL_LANE);

                    // A pedal whose note-off shared its note-on's tick could not check
                    // the (deferred) downchart bit yet; complete its validation now
                    if ((tickDeferredPedalOffs & (1 << diffIndex)) != 0)
                    {
                        downchartValidations |= (DifficultyMask) (1 << (diffIndex + 1));
                    }
                }

                tickHatPedals = 0;
                tickEligibleHatPedals = 0;
                tickStrictHatPedals = 0;
                tickHiHats = 0;
                tickDeferredPedalOffs = 0;
            }

            var note = default(MidiNote);
            var stats = default(MidiStats);
            while (track.ParseEvent(ref stats))
            {
                // Text events can switch the track into strict hat pedal state from
                // this point on, exactly like the full reader's process-map switch
                if (IsTextMetaEvent(stats.Type))
                {
                    if (!strictHatPedalState && IsStrictHatPedalStateText(track.ExtractTextOrSysEx().Span))
                    {
                        strictHatPedalState = true;
                    }
                    continue;
                }

                if (stats.Type != MidiEventType.Note_On && stats.Type != MidiEventType.Note_Off)
                {
                    continue;
                }

                // A new tick means the previous tick's chord context is complete
                if (stats.Position != currentTick)
                {
                    FinalizeTick();
                    currentTick = stats.Position;
                }

                track.ExtractMidiNote(ref note);

                if (IsIndifferentHatMarker(note.Value))
                {
                    var block = note.Value / 24;
                    // Note Ons with no velocity equates to a note Off by spec
                    if (stats.Type == MidiEventType.Note_On && note.Velocity > 0)
                    {
                        openIndifferentMarkerValues |= 1 << block;
                    }
                    else
                    {
                        openIndifferentMarkerValues &= ~(1 << block);
                    }
                    continue;
                }

                if (note.Value == DOUBLE_KICK_NOTE)
                {
                    if ((validations & DifficultyMask.ExpertPlus) > 0)
                    {
                        continue;
                    }

                    // Note Ons with no velocity equates to a note Off by spec
                    if (stats.Type == MidiEventType.Note_On && note.Velocity > 0)
                    {
                        statusBitMask |= DOUBLE_KICK_MASK;
                        downchartStatusBitMask |= DOUBLE_KICK_MASK;
                    }
                    // NoteOff here
                    else if ((statusBitMask & DOUBLE_KICK_MASK) > 0)
                    {
                        validations |= DifficultyMask.Expert | DifficultyMask.ExpertPlus;
                        downchartValidations |= DifficultyMask.Expert | DifficultyMask.ExpertPlus;
                    }
                }

                // Minimum is 0, so no minimum check required
                if (note.Value > ELITE_MAX)
                {
                    continue;
                }

                int diffIndex = MidiPreparser_Constants.EXTENDED_DIFF_INDICES[note.Value];
                int laneIndex = MidiPreparser_Constants.EXTENDED_LANE_INDICES[note.Value];
                var diffMask = (DifficultyMask) (1 << (diffIndex + 1));
                if ((validations & downchartValidations & diffMask) > 0 || laneIndex >= NUM_LANES)
                {
                    continue;
                }

                long statusMask = 1L << (diffIndex * NUM_LANES + laneIndex);
                long downchartStatusMask = 1L << (diffIndex * NUM_LANES + laneIndex);

                // Note Ons with no velocity equates to a note Off by spec
                if (stats.Type == MidiEventType.Note_On && note.Velocity > 0)
                {
                    statusBitMask |= statusMask;

                    if (laneIndex == HAT_PEDAL_LANE)
                    {
                        // Hat pedal notes only contribute to a downchart difficulty
                        // when they are channel flagged to a cymbal AND not ghost
                        // velocity (ghost hat pedals are "invisible terminators",
                        // which the downchart builder never converts, even when
                        // channel flagged) AND not suppressed by their chord
                        // context. That context is complete only once the whole
                        // tick has been read, so the decision is deferred to
                        // FinalizeTick. This mask must agree with MoonSongLoader's
                        // generated-downchart emptiness check, not just with note
                        // presence.
                        tickHatPedals |= 1 << diffIndex;
                        if (stats.Channel is CYMBAL_CHANNEL_FLAG_Y or CYMBAL_CHANNEL_FLAG_B or CYMBAL_CHANNEL_FLAG_G
                            && note.Velocity != MidIOHelper.VELOCITY_GHOST)
                        {
                            tickEligibleHatPedals |= 1 << diffIndex;
                        }

                        if (strictHatPedalState)
                        {
                            tickStrictHatPedals |= 1 << diffIndex;
                        }
                    }
                    else
                    {
                        if (laneIndex == HIHAT_LANE)
                        {
                            tickHiHats |= 1 << diffIndex;
                        }

                        downchartStatusBitMask |= downchartStatusMask;
                    }
                }
                // Note off here
                else
                {
                    if ((statusBitMask & statusMask) > 0)
                    {
                        validations |= diffMask;
                    }

                    if (laneIndex == HAT_PEDAL_LANE && (tickHatPedals & (1 << diffIndex)) != 0)
                    {
                        // The matching note-on is at this same tick, so its
                        // downchart bit has not been decided yet; FinalizeTick
                        // completes the validation once the tick ends
                        tickDeferredPedalOffs |= 1 << diffIndex;
                    }
                    else if ((downchartStatusBitMask & downchartStatusMask) > 0)
                    {
                        downchartValidations |= diffMask;
                    }

                    if ((validations & downchartValidations) == MidiPreparser_Constants.ALL_DIFFICULTIES_PLUS)
                    {
                        break;
                    }
                }

            }

            // The final tick's chord context is complete at end of track
            FinalizeTick();

            // Add beginner to the mask if easy is present (mask easy, shift right, or result with validations)
            validations |= (DifficultyMask)((int)(validations & DifficultyMask.Easy) >> 1);
            downchartValidations |= (DifficultyMask)((int)(downchartValidations & DifficultyMask.Easy) >> 1);

            return (validations, downchartValidations);
        }

        private static bool IsIndifferentHatMarker(int noteValue)
        {
            return noteValue is INDIFFERENT_HAT_MARKER_E or INDIFFERENT_HAT_MARKER_M
                or INDIFFERENT_HAT_MARKER_H or INDIFFERENT_HAT_MARKER_X;
        }

        // Mirrors MidIOHelper.IsTextEvent: text meta events are candidates for
        // parsing modifications, except track names and copyright notices
        private static bool IsTextMetaEvent(MidiEventType type)
        {
            return type >= MidiEventType.Text && type <= MidiEventType.Text_CuePoint
                && type != MidiEventType.Text_TrackName
                && type != MidiEventType.Text_Copyright;
        }

        private static bool IsStrictHatPedalStateText(ReadOnlySpan<byte> text)
        {
            // Byte-level mirror of TextEvents.NormalizeTextEvent: isolate any
            // bracketed text, trim ASCII whitespace, then compare against the
            // strict-hat-pedal-state text (valid with and without brackets)
            var start = text.IndexOf((byte) '[');
            var end = text.IndexOf((byte) ']');
            if (start >= 0 && end >= 0 && start <= end)
            {
                text = text.Slice(start + 1, end - start - 1);
            }

            return TrimAscii(text).SequenceEqual(STRICT_HAT_PEDAL_STATE_TEXT);
        }

        private static ReadOnlySpan<byte> TrimAscii(ReadOnlySpan<byte> text)
        {
            var start = 0;
            var end = text.Length;
            while (start < end && IsAsciiWhitespace(text[start]))
            {
                start++;
            }

            while (end > start && IsAsciiWhitespace(text[end - 1]))
            {
                end--;
            }

            return text.Slice(start, end - start);
        }

        private static bool IsAsciiWhitespace(byte value)
        {
            return value is (byte) ' ' or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D;
        }
    }
}
