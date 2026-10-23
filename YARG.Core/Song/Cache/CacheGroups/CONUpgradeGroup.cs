using System;
using System.Collections.Generic;
using System.IO;
using YARG.Core.Extensions;
using YARG.Core.IO;
using YARG.Core.Logging;

namespace YARG.Core.Song.Cache
{
    internal class PackedCONUpgradeGroup
    {
        private readonly Dictionary<string, (YARGTextContainer<byte> Container, PackedRBProUpgrade Upgrade)> _upgrades = new();
        private AbridgedFileInfo _root;
        private FixedArray<byte>? _data;

        public AbridgedFileInfo Root => _root;
        public Dictionary<string, (YARGTextContainer<byte> Container, PackedRBProUpgrade Upgrade)> Upgrades => _upgrades;

        public void Dispose()
        {
            _data?.Dispose();
        }

        private PackedCONUpgradeGroup() { }

        public static bool Create(Stream stream, List<CONFileListing> listings, in AbridgedFileInfo root, out PackedCONUpgradeGroup group)
        {
            const string UPGRADES_PATH = PackedRBProUpgrade.UPGRADES_DIRECTORY + RBProUpgrade.UPGRADES_DTA;
            group = null!;
            if (listings.FindListing(UPGRADES_PATH, out var listing))
            {
                group = new PackedCONUpgradeGroup()
                {
                    _root = root,
                };

                using var data = CONFileStream.LoadFile(stream, listing);

                var container = YARGDTAReader.Create(data);
                while (YARGDTAReader.StartNode(ref container))
                {
                    string name = YARGDTAReader.GetNameOfNode(ref container, true);
                    if (listings.FindListing(PackedRBProUpgrade.UPGRADES_DIRECTORY + name + RBProUpgrade.UPGRADES_MIDI_EXT, out listing))
                    {
                        group._upgrades[name] = (container, new PackedRBProUpgrade(listing, root));
                    }
                    YARGDTAReader.EndNode(ref container);
                }
                group._data = data.TransferOwnership();
            }
            return group != null;
        }

        public static void SerializeGroups(FileStream fileStream, List<PackedCONUpgradeGroup> groups)
        {
            using var groupStream = new MemoryStream();
            fileStream.Write(groups.Count, Endianness.Little);
            for (int i = 0; i < groups.Count; i++)
            {
                groupStream.SetLength(0);
                groups[i]._root.Serialize(groupStream);
                groupStream.Write(groups[i]._upgrades.Count, Endianness.Little);
                foreach (var node in groups[i]._upgrades)
                {
                    groupStream.Write(node.Key);
                }
                fileStream.Write((int) groupStream.Length, Endianness.Little);
                fileStream.Write(groupStream.GetBuffer(), 0, (int) groupStream.Length);
            }
        }
    }

    internal class UnpackedCONUpgradeGroup
    {
        private readonly Dictionary<string, (YARGTextContainer<byte> Container, UnpackedRBProUpgrade Upgrade)> _upgrades = new();
        private AbridgedFileInfo _root;
        private FixedArray<byte>? _data;

        public AbridgedFileInfo Root => _root;
        public Dictionary<string, (YARGTextContainer<byte> Container, UnpackedRBProUpgrade Upgrade)> Upgrades => _upgrades;

        public void Dispose()
        {
            _data?.Dispose();
        }

        public string DtaPath { get; private set; } = string.Empty;

        private UnpackedCONUpgradeGroup() { }

        public static bool Create(in FileCollection collection, FileInfo dtaInfo, out UnpackedCONUpgradeGroup group, byte[]? sourceData = null)
        {
            group = new UnpackedCONUpgradeGroup()
            {
                _root = new AbridgedFileInfo(collection.Directory, AbridgedFileInfo.NormalizedLastWrite(dtaInfo)),
                DtaPath = dtaInfo.FullName
            };

            try
            {
                using var data = sourceData == null ? FixedArray.LoadFile(dtaInfo.FullName) : FixedArray<byte>.Alloc(sourceData.Length);
                if (sourceData != null) sourceData.AsSpan().CopyTo(data.Span);

                var container = YARGDTAReader.Create(data);
                while (YARGDTAReader.StartNode(ref container))
                {
                    string name = YARGDTAReader.GetNameOfNode(ref container, true);
                    if (collection.FindFile(name.ToLower() + RBProUpgrade.UPGRADES_MIDI_EXT, out var info))
                    {
                        group._upgrades[name] = (container, new UnpackedRBProUpgrade(name, AbridgedFileInfo.NormalizedLastWrite(info), group._root, dtaInfo.FullName));
                    }
                    YARGDTAReader.EndNode(ref container);
                }
                group._data = data.TransferOwnership();
                return true;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e);
                group = null!;
                return false;
            }
        }

        internal static UnpackedCONUpgradeGroup CreateExplicit(FileInfo dtaInfo, byte[] sourceData)
        {
            var group = new UnpackedCONUpgradeGroup
            {
                _root = new AbridgedFileInfo(dtaInfo.DirectoryName!, AbridgedFileInfo.NormalizedLastWrite(dtaInfo)),
                DtaPath = dtaInfo.FullName,
                _data = FixedArray<byte>.Alloc(sourceData.Length)
            };
            try
            {
                sourceData.AsSpan().CopyTo(group._data.Span);
                var text = YARGDTAReader.Create(group._data);
                while (YARGDTAReader.StartNode(ref text))
                {
                    string name = YARGDTAReader.GetNameOfNode(ref text, true);
                    var midi = new FileInfo(Path.Combine(dtaInfo.DirectoryName!, name + RBProUpgrade.UPGRADES_MIDI_EXT));
                    if (!midi.Exists) throw new FileNotFoundException("Upgrade MIDI missing: " + midi.FullName);
                    group._upgrades[name] = (text, new UnpackedRBProUpgrade(name, AbridgedFileInfo.NormalizedLastWrite(midi), group._root, dtaInfo.FullName));
                    YARGDTAReader.EndNode(ref text);
                }
                return group;
            }
            catch { group.Dispose(); throw; }
        }

        public static void SerializeGroups(FileStream fileStream, List<UnpackedCONUpgradeGroup> groups)
        {
            using var groupStream = new MemoryStream();
            fileStream.Write(groups.Count, Endianness.Little);
            for (int i = 0; i < groups.Count; i++)
            {
                groupStream.SetLength(0);
                groups[i]._root.Serialize(groupStream);
                groupStream.Write(groups[i].DtaPath);
                groupStream.Write(groups[i]._upgrades.Count, Endianness.Little);
                foreach (var node in groups[i]._upgrades)
                {
                    groupStream.Write(node.Key);
                    groupStream.Write(node.Value.Upgrade.LastWriteTime.ToBinary(), Endianness.Little);
                }
                fileStream.Write((int) groupStream.Length, Endianness.Little);
                fileStream.Write(groupStream.GetBuffer(), 0, (int) groupStream.Length);
            }
        }
    }
}
