// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Input;

namespace YARG.Core.Engine.Drums.Engines
{
    public partial class EliteDrumsEngine
    {
        public IReadOnlyList<EliteHiHatSustain> HiHatSustains { get; private set; } = Array.Empty<EliteHiHatSustain>();
        public EliteHiHatSustain? ActiveHiHatSustain { get; private set; }
        public bool IsHiHatSustainOverhit { get; private set; }
        public bool IsHiHatSustainRelease { get; private set; }
        /// <summary>Raised after a ringing interval becomes active, with its activation engine time.</summary>
        public Action<EliteHiHatSustain, double>? OnHiHatSustainStarted;
        public Action<EliteHiHatSustain, bool>? OnHiHatSustainEnded;
        private readonly List<(double Start, double End)> _hiHatPedalRolls = new();
        private readonly Dictionary<EliteDrumNote, EliteHiHatSustain> _hiHatGenerators = new();
        private readonly HashSet<EliteHiHatSustain> _brokenHiHatSustains = new();

        private sealed class HiHatScore
        {
            public double Start, End;
            public int Budget, Awarded, Multiplier, BandBonus;
            public bool StarPower;
        }
        private readonly List<HiHatScore> _hiHatScores = new();

        private bool StartHiHatSustainScore(EliteDrumNote note, int articulationDivisor, bool closedAsOpen)
        {
            if (EngineParameters.EliteHiHatSustains < 2 || !_hiHatGenerators.TryGetValue(note, out var sustain) ||
                CurrentTime >= sustain.EndTime) return false;
            int head = POINTS_PER_NOTE / 2 / articulationDivisor;
            AddScore(head);
            EngineStats.NoteScore += head;
            if (closedAsOpen) return true;
            _hiHatScores.Add(new HiHatScore {
                Start = Math.Max(CurrentTime, sustain.StartTime), End = sustain.EndTime,
                Multiplier = EngineStats.ScoreMultiplier, StarPower = EngineStats.IsStarPowerActive,
                BandBonus = EngineStats.BandBonusMultiplier,
                Budget = POINTS_PER_NOTE / articulationDivisor - head
            });
            return true;
        }

        private void AwardHiHatSustainScore(double time, bool complete = false)
        {
            foreach (var score in _hiHatScores)
            {
                double progress = complete ? 1 : Math.Clamp((time - score.Start) / (score.End - score.Start), 0, 1);
                int earned = (int)Math.Floor(score.Budget * progress);
                int delta = earned - score.Awarded;
                if (delta <= 0) continue;
                AddScore(delta, score.Multiplier, score.StarPower, score.BandBonus);
                EngineStats.SustainScore += delta * (score.StarPower ? score.Multiplier / 2 : score.Multiplier);
                score.Awarded = earned;
            }
        }

        public bool WasHiHatSustainBroken(EliteHiHatSustain sustain) => _brokenHiHatSustains.Contains(sustain);

        private void ForfeitHiHatSustain(EliteDrumNote note)
        {
            if (_hiHatGenerators.TryGetValue(note, out var sustain) &&
                !ReferenceEquals(ActiveHiHatSustain, sustain))
                _brokenHiHatSustains.Add(sustain);
        }

        private void InitializeHiHatSustains(InstrumentDifficulty<EliteDrumNote> chart, SyncTrack sync)
        {
            if (EngineParameters.EliteHiHatSustains == 0) return;
            HiHatSustains = EliteHiHatSustain.Generate(chart, sync);
            if (EngineParameters.EnableLanes)
                foreach (var segment in EliteDrumsPlayableLaneSegments.Resolve(chart))
                    if (segment.Pad == (int)EliteDrumNote.EliteDrumPad.HatPedal && segment.Members.Count >= 2)
                        _hiHatPedalRolls.Add((segment.StartTime, segment.EndTime));
            foreach (var sustain in HiHatSustains)
                foreach (var note in sustain.Generators) _hiHatGenerators.Add(note, sustain);
        }

        private void ResetHiHatSustains()
        {
            ActiveHiHatSustain = null;
            _brokenHiHatSustains?.Clear();
            IsHiHatSustainOverhit = false;
            IsHiHatSustainRelease = false;
            _hiHatScores?.Clear();
        }

        private void AdvanceHiHatSustain(double time)
        {
            AwardHiHatSustainScore(time);
            if (ActiveHiHatSustain is { } active && (time >= active.EndTime || IsCodaActive))
                EndHiHatSustain(false);
        }

        private void EndHiHatSustain(bool broken)
        {
            if (ActiveHiHatSustain is not { } active) return;
            if (!broken) AwardHiHatSustainScore(CurrentTime, complete: true);
            _hiHatScores.Clear();
            ActiveHiHatSustain = null;
            if (broken) _brokenHiHatSustains.Add(active);
            OnHiHatSustainEnded?.Invoke(active, broken);
        }

        private EliteHiHatSustain? HitHiHatSustain(EliteDrumNote note)
        {
            if (_hiHatGenerators.TryGetValue(note, out var sustain) && CurrentTime < sustain.EndTime)
            {
                bool started = !ReferenceEquals(ActiveHiHatSustain, sustain);
                if (ActiveHiHatSustain != null && started)
                    EndHiHatSustain(false);
                _brokenHiHatSustains.Remove(sustain);
                ActiveHiHatSustain = sustain;
                return started ? sustain : null;
            }
            else if (EliteHiHatSustain.IsTerminator(note))
                EndHiHatSustain(false);
            return null;
        }

        private double HiHatSustainEarlyEnd(EliteHiHatSustain sustain)
        {
            // Use the ordinary local note-density window at the endpoint, including
            // the chosen preset, asymmetric front end and song-speed conversion.
            EliteDrumNote? before = null, after = null;
            foreach (var note in Notes)
            {
                if (note.Time >= sustain.EndTime) { after = note; break; }
                before = note;
            }
            double distance = after != null && after.Time == sustain.EndTime ? GetAverageNoteDistance(after) :
                before != null && after != null ? (after.Time - before.Time) / 2 :
                before != null ? (sustain.EndTime - before.Time + EngineParameters.HitWindow.MaxWindow) / 2 :
                after != null ? after.Time - sustain.EndTime : EngineParameters.HitWindow.MaxWindow;
            double full = EngineParameters.HitWindow.CalculateHitWindow(distance);
            return sustain.EndTime + EngineParameters.HitWindow.GetFrontEnd(full);
        }

        private bool WouldBreakHiHatSustain(double time) => ActiveHiHatSustain is { } sustain &&
            time < HiHatSustainEarlyEnd(sustain) && !IsCodaActive &&
            !_hiHatPedalRolls.Any(roll => time >= roll.Start && time <= roll.End);

        private bool TryBreakHiHatSustain(EliteDrumsAction action)
        {
            if (action is not (EliteDrumsAction.EliteStomp or EliteDrumsAction.EliteSplash)) return false;
            if (EngineParameters.EliteHiHatSustains >= 2)
            {
                if (ActiveHiHatSustain == null || IsCodaActive ||
                    _hiHatPedalRolls.Any(roll => CurrentTime >= roll.Start && CurrentTime <= roll.End)) return false;
                EndHiHatSustain(WouldBreakHiHatSustain(CurrentTime));
                IsHiHatSustainRelease = true;
                try { OnPadHit?.Invoke(action, false, false, false, DrumNoteType.Neutral, _velocity.GetValueOrDefault()); }
                finally { IsHiHatSustainRelease = false; }
                return true;
            }
            if (!WouldBreakHiHatSustain(CurrentTime)) return false;
            EndHiHatSustain(true);
            EngineStats.HiHatSustainBreaks++;
            IsHiHatSustainOverhit = true;
            try
            {
                OnPadHit?.Invoke(action, false, false, false, DrumNoteType.Neutral, _velocity.GetValueOrDefault());
                Overhit();
            }
            finally { IsHiHatSustainOverhit = false; }
            return true;
        }
    }
}
