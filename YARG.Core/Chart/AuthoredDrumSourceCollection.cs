using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using YARG.Core.Game;

namespace YARG.Core.Chart
{
    /// <summary>Native authored drum tiers, before extra-kick filtering or Beginner collapse.</summary>
    public sealed class AuthoredDrumSourceCollection
    {
        private readonly Dictionary<(DrumSourceFormat Source, Difficulty Tier), AuthoredDrumSourceTier> _tiers = new();
        public IReadOnlyDictionary<(DrumSourceFormat Source, Difficulty Tier), AuthoredDrumSourceTier> Tiers { get; }

        public AuthoredDrumSourceCollection()
        {
            Tiers = new ReadOnlyDictionary<(DrumSourceFormat Source, Difficulty Tier), AuthoredDrumSourceTier>(_tiers);
        }

        internal void Add(AuthoredDrumSourceTier tier) => _tiers.Add((tier.Facts.Source, tier.Facts.Tier), tier);

        internal void Append(AuthoredDrumSourceCollection sources)
        {
            foreach (var entry in sources.Tiers)
                _tiers[entry.Key] = entry.Value;
        }

        public bool TryGetTier(DrumSourceFormat source, Difficulty tier, out AuthoredDrumSourceTier? authoredTier)
            => _tiers.TryGetValue((source, tier), out authoredTier);
    }

    /// <summary>A source tier owns its notes; gameplay transforms must work on a clone.</summary>
    public sealed class AuthoredDrumSourceTier
    {
        private readonly InstrumentDifficulty<DrumNote>? _classic;
        private readonly InstrumentDifficulty<EliteDrumNote>? _elite;
        public DrumSourceTierFacts Facts { get; }
        /// <summary>Ticks where an ordinary and extra Elite kick were merged by the MIDI parser.</summary>
        public IReadOnlyList<uint> PairedKickTicks { get; }

        internal AuthoredDrumSourceTier(DrumSourceTierFacts facts, InstrumentDifficulty<DrumNote>? classic,
            InstrumentDifficulty<EliteDrumNote>? elite, IEnumerable<uint> pairedKickTicks)
        {
            Facts = facts;
            _classic = classic?.Clone();
            _elite = elite?.Clone();
            PairedKickTicks = new List<uint>(pairedKickTicks).AsReadOnly();
        }

        public InstrumentDifficulty<DrumNote> CloneClassicDifficulty()
            => _classic?.Clone() ?? throw new InvalidOperationException("This is an Elite drum source tier.");

        public InstrumentDifficulty<EliteDrumNote> CloneEliteDifficulty()
            => _elite?.Clone() ?? throw new InvalidOperationException("This is a classic drum source tier.");
    }
}
