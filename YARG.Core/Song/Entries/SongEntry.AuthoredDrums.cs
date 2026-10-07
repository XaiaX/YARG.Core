using System;
using System.Collections.Generic;
using System.IO;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Extensions;
using YARG.Core.IO;

namespace YARG.Core.Song
{
    public abstract partial class SongEntry
    {
        // Reuse the native source loader, never the public playable/generated tracks. This
        // deliberately pays full parsing cost at scan time to keep parser note semantics,
        // paired kicks and suppressed Elite pedals identical to chart loading.
        internal void CollectAuthoredDrumSourceFacts(MoonSongLoader loader)
        {
            var facts = new List<DrumSourceTierFacts>();
            foreach (var tier in loader.LoadAuthoredDrumSources().Tiers.Values)
                facts.Add(tier.Facts);
            _authoredDrumSourceFacts = facts.AsReadOnly();
        }

        private void SerializeAuthoredDrumSourceFacts(MemoryStream stream)
        {
            stream.Write(_authoredDrumSourceFacts.Count, Endianness.Little);
            foreach (var facts in _authoredDrumSourceFacts)
            {
                stream.WriteByte((byte) facts.Source);
                stream.WriteByte((byte) facts.Tier);
                stream.WriteByte((byte) ((facts.OrdinaryKick ? 1 : 0) | (facts.ExtraKick ? 2 : 0) |
                    (facts.OtherPlayable ? 4 : 0) | (facts.PairedKickFlam ? 8 : 0) | (facts.ClassicEligible ? 16 : 0)));
            }
        }

        private void DeserializeAuthoredDrumSourceFacts(ref FixedArrayStream stream)
        {
            int count = stream.Read<int>(Endianness.Little);
            // Three source domains, each with Easy through Expert. Reject corrupt lengths
            // before allocating, and reject duplicate identities rather than hiding them.
            if (count < 0 || count > 12)
                throw new InvalidDataException("Invalid authored drum tier count.");
            var facts = new List<DrumSourceTierFacts>(count);
            var identities = new HashSet<(DrumSourceFormat, Difficulty)>();
            for (int i = 0; i < count; i++)
            {
                var source = (DrumSourceFormat) stream.ReadByte();
                var tier = (Difficulty) stream.ReadByte();
                int flags = stream.ReadByte();
                if ((flags & ~31) != 0 || !Enum.IsDefined(typeof(DrumSourceFormat), source) ||
                    tier < Difficulty.Easy || tier > Difficulty.Expert || !identities.Add((source, tier)))
                    throw new InvalidDataException("Invalid authored drum tier facts.");
                facts.Add(new DrumSourceTierFacts(source, tier, (flags & 1) != 0, (flags & 2) != 0,
                    (flags & 4) != 0, (flags & 8) != 0, (flags & 16) != 0));
            }
            _authoredDrumSourceFacts = facts.AsReadOnly();
        }
    }
}
