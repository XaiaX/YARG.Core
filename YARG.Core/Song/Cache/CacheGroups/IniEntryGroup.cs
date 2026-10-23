using System.Collections.Generic;
using System.IO;
using YARG.Core.Extensions;

namespace YARG.Core.Song.Cache
{
    internal sealed class IniEntryGroup : IEntryGroup
    {
        private readonly string _directory;
        private readonly List<UnpackedIniEntry> _unpacked = new();
        private readonly List<SngEntry> _packed = new();

        public string Directory => _directory;
        internal IEnumerable<SongEntry> CachedEntries
        {
            get { foreach (var entry in _unpacked) yield return entry; foreach (var entry in _packed) yield return entry; }
        }
        internal void AddEntry(SongEntry entry)
        {
            if (entry is UnpackedIniEntry unpacked) AddEntry(unpacked);
            else if (entry is SngEntry packed) AddEntry(packed);
            else throw new System.ArgumentException("Not an ini or SNG entry", nameof(entry));
        }

        public IniEntryGroup(string directory)
        {
            _directory = directory;
        }

        public void AddEntry(UnpackedIniEntry entry)
        {
            lock (_unpacked)
            {
                _unpacked.Add(entry);
            }
        }

        public void AddEntry(SngEntry entry)
        {
            lock (_packed)
            {
                _packed.Add(entry);
            }
        }

        public void Serialize(MemoryStream groupStream, Dictionary<SongEntry, CacheWriteIndices> nodes)
        {
            groupStream.Write(_directory);
            using MemoryStream entryStream = new();
            SerializeList(entryStream, _unpacked, groupStream, nodes);
            SerializeList(entryStream, _packed, groupStream, nodes);
        }

        private void SerializeList<TEntry>(MemoryStream entryStream, List<TEntry> entries, MemoryStream groupStream, Dictionary<SongEntry, CacheWriteIndices> nodes)
            where TEntry : IniSubEntry
        {
            groupStream.Write(entries.Count, Endianness.Little);
            foreach (var entry in entries)
            {
                entryStream.SetLength(0);

                // Validation block
                string relativePath = Path.GetRelativePath(_directory, entry.ActualLocation);
                if (relativePath == ".")
                {
                    relativePath = string.Empty;
                }
                entryStream.Write(relativePath);

                entry.Serialize(entryStream, nodes[entry]);

                groupStream.Write((int) entryStream.Length, Endianness.Little);
                groupStream.Write(entryStream.GetBuffer(), 0, (int) entryStream.Length);
            }
        }
    }
}
