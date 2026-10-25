// pattern: Imperative Shell
using System;
using System.Collections.Generic;

namespace YARG.Core.Song.Supplements
{
    /// <summary>Process-local LRU for the active update and upgrade indexes. File handles
    /// are never cached. Keys are freshly hashed manifest bytes, not path timestamps.</summary>
    internal static class SupplementPackageIndexCache
    {
        private const int CAPACITY = 2;
        private static readonly List<SupplementPackageIndex> Indexes = new();

        internal static SupplementPackageIndex Get(string hash, Func<SupplementPackageIndex> create)
        {
            lock (Indexes)
            {
                for (int i = 0; i < Indexes.Count; i++)
                {
                    var found = Indexes[i];
                    if (found.ManifestHash != hash) continue;
                    Indexes.RemoveAt(i); Indexes.Insert(0, found);
                    return found;
                }
                // Build under the lock so simultaneous readers do not duplicate a cold parse.
                var index = create();
                Indexes.Insert(0, index);
                if (Indexes.Count > CAPACITY) Indexes.RemoveAt(Indexes.Count - 1);
                return index;
            }
        }
    }
}
