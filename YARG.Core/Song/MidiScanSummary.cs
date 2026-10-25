// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Text;
using YARG.Core.Chart;
using YARG.Core.IO;

namespace YARG.Core.Song
{
    public enum MidiSummaryScanResult { Success, InvalidResolution, MultipleTrackNames, Unsupported }

    /// <summary>Immutable, event-free observations for library scanning. Does not represent a playable chart.</summary>
    public sealed partial class MidiScanSummary
    {
        public const int SEMANTICS_VERSION = 1;
        public long Resolution { get; }
        public int TrackCount => _tracks.Length;
        private readonly TrackObservation[] _tracks;

        private MidiScanSummary(long resolution, TrackObservation[] tracks)
        {
            Resolution = resolution;
            _tracks = tracks;
        }

        /// <summary>
        /// Compile once from structurally valid MIDI bytes. The input buffer is not retained.
        /// Invalid headers or track-name event streams may throw: callers must retain the raw scanner
        /// fallback. Compiling all tracks can encounter malformed data after an earlier name conflict
        /// that makes the raw scanner return early. This API is not an input validator.
        /// </summary>
        public static MidiScanSummary Compile(FixedArray<byte> bytes)
        {
            var file = YARGMidiFile.Load(bytes);
            if (file.Resolution == 0) return new MidiScanSummary(0, Array.Empty<TrackObservation>());
            var tracks = new List<TrackObservation>();
            while (file.GetNextTrack(out _, out var track))
            {
                var observation = new TrackObservation();
                // Merge uses the FIRST tick-zero name; scanning uses the last consistent name.
                var nameReader = track;
                var stats = default(MidiStats);
                while (nameReader.ParseEvent(ref stats) && stats.Position == 0)
                {
                    if (stats.Type == MidiEventType.Text_TrackName)
                    {
                        observation.MergeName = nameReader.ExtractTextOrSysEx().GetString(YARGTextReader.Latin1);
                        break;
                    }
                }
                observation.ValidName = track.FindTrackName(out var name);
                if (observation.ValidName && YARGMidiTrack.TRACKNAMES.TryGetValue(name.GetString(Encoding.ASCII), out var type))
                {
                    observation.Type = type;
                    try { Observe(track, observation); }
                    catch (Exception error) when (!(error is OutOfMemoryException))
                    {
                        // A skipped duplicate may be malformed without affecting the raw scan.
                        // Request fallback only if this observation is actually used.
                        observation.Unsupported = true;
                    }
                }
                tracks.Add(observation);
            }
            return new MidiScanSummary(file.Resolution, tracks.ToArray());
        }

        /// <summary>Apply the same first-name replacement rules as MidiExtensions.Merge. Neither input is mutated.</summary>
        public MidiScanSummary Merge(MidiScanSummary supplement, bool copyVenue = false)
        {
            if (supplement == null) throw new ArgumentNullException(nameof(supplement));
            var tracks = new List<TrackObservation>(_tracks);
            for (int i = 1; i < supplement._tracks.Length; i++)
            {
                var source = supplement._tracks[i];
                int target = -1;
                if (!string.IsNullOrEmpty(source.MergeName))
                {
                    for (int j = 1; j < tracks.Count; j++)
                    {
                        if (source.MergeName == tracks[j].MergeName) { target = j; break; }
                    }
                }
                if (target < 0) tracks.Add(source);
                else if (copyVenue || source.MergeName != "VENUE") tracks[target] = source;
            }
            return new MidiScanSummary(Resolution, tracks.ToArray());
        }

        /// <summary>
        /// Apply observations using the raw scanner's stateful precedence. Unsupported restores both
        /// inputs so the caller can retry the raw scanner without partially applied observations.
        /// </summary>
        public MidiSummaryScanResult Scan(ref AvailableParts parts, ref DrumsType drumsType)
        {
            var originalParts = parts;
            var originalDrums = drumsType;
            try { return ScanCore(ref parts, ref drumsType); }
            catch (UnsupportedObservationException)
            {
                parts = originalParts;
                drumsType = originalDrums;
                return MidiSummaryScanResult.Unsupported;
            }
        }

        private MidiSummaryScanResult ScanCore(ref AvailableParts parts, ref DrumsType drumsType)
        {
            if (Resolution == 0) return MidiSummaryScanResult.InvalidResolution;
            if ((uint) drumsType > 7) throw new ArgumentOutOfRangeException(nameof(drumsType));
            var nativeDrumDifficulties = DifficultyMask.None;
            bool harm2 = false;
            bool harm3 = false;
            foreach (var track in _tracks)
            {
                if (!track.ValidName) return MidiSummaryScanResult.MultipleTrackNames;
                var type = track.Type;
                switch (type)
                {
                    case MidiTrackType.Guitar_5: if (!parts.FiveFretGuitar.IsActive())     parts.FiveFretGuitar.Difficulties     = track.GetDifficulties(); break;
                    case MidiTrackType.Bass_5:   if (!parts.FiveFretBass.IsActive())       parts.FiveFretBass.Difficulties       = track.GetDifficulties(); break;
                    case MidiTrackType.Rhythm_5: if (!parts.FiveFretRhythm.IsActive())     parts.FiveFretRhythm.Difficulties     = track.GetDifficulties(); break;
                    case MidiTrackType.Coop_5:   if (!parts.FiveFretCoopGuitar.IsActive()) parts.FiveFretCoopGuitar.Difficulties = track.GetDifficulties(); break;
                    case MidiTrackType.Keys:     if (!parts.Keys.IsActive())               parts.Keys.Difficulties               = track.GetDifficulties(); break;

                    case MidiTrackType.Guitar_6: if (!parts.SixFretGuitar.IsActive())      parts.SixFretGuitar.Difficulties      = track.GetDifficulties(); break;
                    case MidiTrackType.Bass_6:   if (!parts.SixFretBass.IsActive())        parts.SixFretBass.Difficulties        = track.GetDifficulties(); break;
                    case MidiTrackType.Rhythm_6: if (!parts.SixFretRhythm.IsActive())      parts.SixFretRhythm.Difficulties      = track.GetDifficulties(); break;
                    case MidiTrackType.Coop_6:   if (!parts.SixFretCoopGuitar.IsActive())  parts.SixFretCoopGuitar.Difficulties  = track.GetDifficulties(); break;

                    case MidiTrackType.Drums:
                        if (nativeDrumDifficulties == DifficultyMask.None)
                            { track.EnsureSupported(); nativeDrumDifficulties = track.DrumMasks[(int) drumsType]; drumsType = track.DrumTypes[(int) drumsType]; }
                        break;
                    case MidiTrackType.EliteDrums:
                        var difficulties = track.GetElite();

                        if (!parts.EliteDrums.IsActive())
                        {
                            parts.EliteDrums.Difficulties = difficulties.eliteDrumsDifficulties;

                            // Record which difficulties the Elite chart actually downcharts
                            // to (at least one converted note). This is the metadata
                            // EliteDrumsDownchartRules consults so Difficulty Select and
                            // Maestro can never offer a target/difficulty whose generated
                            // downchart would be empty (e.g. an Elite chart made only of
                            // unforced/invisible hat pedal notes).
                            parts.EliteDrumsDownchart.Difficulties = difficulties.downchartDifficulties;
                        }

                        break;

                    case MidiTrackType.Pro_Guitar_17: if (!parts.ProGuitar_17Fret.IsActive()) parts.ProGuitar_17Fret.Difficulties = track.GetDifficulties(); break;
                    case MidiTrackType.Pro_Guitar_22: if (!parts.ProGuitar_22Fret.IsActive()) parts.ProGuitar_22Fret.Difficulties = track.GetDifficulties(); break;
                    case MidiTrackType.Pro_Bass_17:   if (!parts.ProBass_17Fret.IsActive())   parts.ProBass_17Fret.Difficulties   = track.GetDifficulties(); break;
                    case MidiTrackType.Pro_Bass_22:   if (!parts.ProBass_22Fret.IsActive())   parts.ProBass_22Fret.Difficulties   = track.GetDifficulties(); break;

                    case MidiTrackType.Pro_Keys_E: if (!parts.ProKeys[Difficulty.Easy]   && track.GetPlayable()) parts.ProKeys.ActivateDifficulty(Difficulty.Easy); break;
                    case MidiTrackType.Pro_Keys_M: if (!parts.ProKeys[Difficulty.Medium] && track.GetPlayable()) parts.ProKeys.ActivateDifficulty(Difficulty.Medium); break;
                    case MidiTrackType.Pro_Keys_H: if (!parts.ProKeys[Difficulty.Hard]   && track.GetPlayable()) parts.ProKeys.ActivateDifficulty(Difficulty.Hard); break;
                    case MidiTrackType.Pro_Keys_X: if (!parts.ProKeys[Difficulty.Expert] && track.GetPlayable()) parts.ProKeys.ActivateDifficulty(Difficulty.Expert); break;

                    case MidiTrackType.Vocals: if (!parts.LeadVocals[0]    && track.GetPlayable())  parts.LeadVocals.ActivateSubtrack(0); break;
                    case MidiTrackType.Harm1:  if (!parts.HarmonyVocals[0] && track.GetPlayable())  parts.HarmonyVocals.ActivateSubtrack(0); break;
                    case MidiTrackType.Harm2:  if (!harm2) harm2 = track.GetPlayable(); break;
                    case MidiTrackType.Harm3:  if (!harm3) harm3 = track.GetPlayable(); break;
                }
            }

            bool fiveLane = !drumsType.Has(DrumsType.FourLane) && !drumsType.Has(DrumsType.ProDrums);
            parts.NativeFourLaneDrums = fiveLane ? DifficultyMask.None : nativeDrumDifficulties;
            parts.NativeFiveLaneDrums = fiveLane ? nativeDrumDifficulties : DifficultyMask.None;
            parts.NativeEliteDrums = parts.EliteDrums.Difficulties;

            // Generated Elite downcharts must not suppress a later native PART DRUMS track.
            parts.FourLaneDrums.Difficulties = nativeDrumDifficulties != DifficultyMask.None
                ? nativeDrumDifficulties : parts.EliteDrumsDownchart.Difficulties;

            // HARM 2/3 are not playable without HARM1 phrases
            if (parts.HarmonyVocals[0])
            {
                if (harm2)
                {
                    parts.HarmonyVocals.ActivateSubtrack(1);
                }
                if (harm3)
                {
                    parts.HarmonyVocals.ActivateSubtrack(2);
                }
            }
            return MidiSummaryScanResult.Success;
        }

        private sealed class UnsupportedObservationException : Exception { }

        private sealed class TrackObservation
        {
            public bool Unsupported;
            public void EnsureSupported() { if (Unsupported) throw new UnsupportedObservationException(); }
            public DifficultyMask GetDifficulties() { EnsureSupported(); return Difficulties; }
            public bool GetPlayable() { EnsureSupported(); return Playable; }
            public (DifficultyMask eliteDrumsDifficulties, DifficultyMask downchartDifficulties) GetElite() { EnsureSupported(); return Elite; }
            public string MergeName = "";
            public bool ValidName;
            public MidiTrackType Type = MidiTrackType.Events;
            public DifficultyMask Difficulties;
            public (DifficultyMask eliteDrumsDifficulties, DifficultyMask downchartDifficulties) Elite;
            public bool Playable;
            public DifficultyMask[] DrumMasks = Array.Empty<DifficultyMask>();
            public DrumsType[] DrumTypes = Array.Empty<DrumsType>();
        }

        private static void Observe(YARGMidiTrack track, TrackObservation observation)
        {
            switch (observation.Type)
            {
                case MidiTrackType.Guitar_5: case MidiTrackType.Bass_5: case MidiTrackType.Rhythm_5:
                case MidiTrackType.Coop_5: case MidiTrackType.Keys:
                    observation.Difficulties = Midi_FiveFret_Preparser.Parse(track); break;
                case MidiTrackType.Guitar_6: case MidiTrackType.Bass_6: case MidiTrackType.Rhythm_6: case MidiTrackType.Coop_6:
                    observation.Difficulties = Midi_SixFret_Preparser.Parse(track); break;
                case MidiTrackType.Pro_Guitar_17: case MidiTrackType.Pro_Bass_17:
                    observation.Difficulties = Midi_ProGuitar_Preparser.Parse_17Fret(track); break;
                case MidiTrackType.Pro_Guitar_22: case MidiTrackType.Pro_Bass_22:
                    observation.Difficulties = Midi_ProGuitar_Preparser.Parse_22Fret(track); break;
                case MidiTrackType.EliteDrums:
                    observation.Elite = Midi_EliteDrums_Preparser.Parse(track); break;
                case MidiTrackType.Drums:
                    observation.DrumMasks = new DifficultyMask[8];
                    observation.DrumTypes = new DrumsType[8];
                    for (int i = 0; i < 8; i++)
                    {
                        var context = (DrumsType) i;
                        observation.DrumMasks[i] = Midi_Drums_Preparser.Parse(track, ref context);
                        observation.DrumTypes[i] = context;
                    }
                    break;
                case MidiTrackType.Pro_Keys_E: case MidiTrackType.Pro_Keys_M: case MidiTrackType.Pro_Keys_H: case MidiTrackType.Pro_Keys_X:
                    observation.Playable = Midi_ProKeys_Preparser.Parse(track); break;
                case MidiTrackType.Vocals: case MidiTrackType.Harm1:
                    observation.Playable = Midi_Vocal_Preparser.Parse(track, true); break;
                case MidiTrackType.Harm2: case MidiTrackType.Harm3:
                    observation.Playable = Midi_Vocal_Preparser.Parse(track, false); break;
            }
        }
    }
}
