using System.Collections.Generic;
using System.IO;
using YARG.Core.Extensions;
using YARG.Core.IO;
using YARG.Core.Replays;

namespace YARG.Core.Engine.Drums
{
    public class DrumsStats : BaseStats
    {
        /// <summary>
        /// Number of overhits which have occurred.
        /// </summary>
        public int Overhits;

        /// <summary>
        /// Number of overhits which have occurred for each drum action.
        /// </summary>
        public readonly Dictionary<int, int> OverhitsByAction = new();

        /// <summary>
        /// Number of ghosts the player hit with correct dynamics.
        /// </summary>
        public int GhostsHit;

        /// <summary>
        /// Total number of ghost notes in the chart.
        /// </summary>
        public int TotalGhosts;

        /// <summary>
        /// Number of accents the player hit with correct dynamics.
        /// </summary>
        public int AccentsHit;

        /// <summary>
        /// Total number of accent notes in the chart.
        /// </summary>
        public int TotalAccents;

        /// <summary>
        /// Amount of points earned from hitting notes with correct dynamics.
        /// </summary>
        public int DynamicsBonus;

        /// <summary>Total optional hi-hat pedal notes on a native Elite chart.</summary>
        public int OptionalPedalNotes;

        /// <summary>Optional hi-hat pedal notes hit by the player.</summary>
        public int OptionalPedalHits;

        /// <summary>Optional hi-hat pedal notes automatically satisfied by assistance.</summary>
        public int AssistedPedalNotes;

        /// <summary>Whether native Elite optional pedal misses are excluded from full combo.</summary>
        public bool OptionalPedalAccuracyEnabled;

        public override bool IsFullCombo => OptionalPedalAccuracyEnabled
            ? NotesMissed == 0
            : base.IsFullCombo;

        public DrumsStats()
        {
        }

        public DrumsStats(DrumsStats stats) : base(stats)
        {
            Overhits = stats.Overhits;
            foreach (var (action, count) in stats.OverhitsByAction)
            {
                OverhitsByAction[action] = count;
            }

            GhostsHit = stats.GhostsHit;
            TotalGhosts = stats.TotalGhosts;
            AccentsHit = stats.AccentsHit;
            TotalAccents = stats.TotalAccents;
            DynamicsBonus = stats.DynamicsBonus;
            OptionalPedalNotes = stats.OptionalPedalNotes;
            OptionalPedalHits = stats.OptionalPedalHits;
            AssistedPedalNotes = stats.AssistedPedalNotes;
            OptionalPedalAccuracyEnabled = stats.OptionalPedalAccuracyEnabled;
        }

        public DrumsStats(ref FixedArrayStream stream, int version)
            : base(ref stream, version)
        {
            Overhits = stream.Read<int>(Endianness.Little);
            GhostsHit = stream.Read<int>(Endianness.Little);
            TotalGhosts = stream.Read<int>(Endianness.Little);
            AccentsHit = stream.Read<int>(Endianness.Little);
            TotalAccents = stream.Read<int>(Endianness.Little);
            DynamicsBonus = stream.Read<int>(Endianness.Little);
            if (version >= ReplayIO.OPTIONAL_PEDAL_STATS_MIN)
            {
                OptionalPedalNotes = stream.Read<int>(Endianness.Little);
                OptionalPedalHits = stream.Read<int>(Endianness.Little);
                AssistedPedalNotes = stream.Read<int>(Endianness.Little);
                OptionalPedalAccuracyEnabled = stream.ReadBoolean();
            }
        }

        public override void Reset()
        {
            base.Reset();
            Overhits = 0;
            OverhitsByAction.Clear();
            GhostsHit = 0;
            // Don't reset TotalGhosts
            // TotalGhosts = 0;

            AccentsHit = 0;
            // Don't reset TotalAccents
            // TotalAccents = 0;

            DynamicsBonus = 0;
            // Preserve chart totals and mode across a reset.
            OptionalPedalHits = 0;
            AssistedPedalNotes = 0;
        }

        public override void Serialize(BinaryWriter writer)
        {
            base.Serialize(writer);

            writer.Write(Overhits);
            writer.Write(GhostsHit);
            writer.Write(TotalGhosts);
            writer.Write(AccentsHit);
            writer.Write(TotalAccents);
            writer.Write(DynamicsBonus);
            writer.Write(OptionalPedalNotes);
            writer.Write(OptionalPedalHits);
            writer.Write(AssistedPedalNotes);
            writer.Write(OptionalPedalAccuracyEnabled);
        }

        public void RecordOverhit(int? action)
        {
            Overhits++;

            if (!action.HasValue)
            {
                return;
            }

            OverhitsByAction[action.Value] = OverhitsByAction.GetValueOrDefault(action.Value) + 1;
        }

        public override ReplayStats ConstructReplayStats(string name, bool isReplayPlayer)
        {
            return new DrumsReplayStats(name, isReplayPlayer, this);
        }
    }
}