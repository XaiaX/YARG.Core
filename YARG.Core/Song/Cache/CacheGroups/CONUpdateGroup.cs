using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using YARG.Core.Extensions;
using YARG.Core.IO;
using YARG.Core.Logging;

namespace YARG.Core.Song.Cache
{
    internal class CONUpdateGroup : IDisposable
    {
        private readonly Dictionary<string, (List<YARGTextContainer<byte>> Containers, DateTime? Update)> _updates = new();
        private AbridgedFileInfo _root;
        private FixedArray<byte> _data = null!;

        public AbridgedFileInfo Root => _root;

        // The package containing songs_updates names the layer. Full paths break ties across roots.
        internal static int CompareLayers(AbridgedFileInfo left, AbridgedFileInfo right)
        {
            string leftPath = Path.GetFullPath(left.FullName).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
            string rightPath = Path.GetFullPath(right.FullName).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
            string leftParent = Path.GetFileName(Path.GetDirectoryName(leftPath));
            string rightParent = Path.GetFileName(Path.GetDirectoryName(rightPath));
            int comparison = CompareStable(leftParent, rightParent);
            return comparison != 0 ? comparison : CompareStable(leftPath, rightPath);
        }

        private static int CompareStable(string? left, string? right)
        {
            int comparison = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left, right);
        }
        public Dictionary<string, (List<YARGTextContainer<byte>> Containers, DateTime? Update)> Updates => _updates;

        // Include file contents, not just timestamps: a full refresh must catch asset-only edits.
        internal string AssetSignature(string node)
        {
            string directory = Path.Combine(_root.FullName, node);
            if (!Directory.Exists(directory))
            {
                return string.Empty;
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                if (file.EndsWith("_update.mid", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file)));
                using var input = File.OpenRead(file);
                byte[] buffer = new byte[81920];
                int length;
                while ((length = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, length);
                }
            }
            return BitConverter.ToString(hash.GetHashAndReset());
        }

        public void Dispose()
        {
            _data.Dispose();
        }

        private CONUpdateGroup() { }

        public static bool Create(string directory, FileInfo dtaInfo, out CONUpdateGroup group)
        {
            try
            {
                group = new CONUpdateGroup()
                {
                    _root = new AbridgedFileInfo(directory, AbridgedFileInfo.NormalizedLastWrite(dtaInfo)),
                };

                using var data = FixedArray.LoadFile(dtaInfo.FullName);

                var container = YARGDTAReader.Create(data);
                while (YARGDTAReader.StartNode(ref container))
                {
                    string name = YARGDTAReader.GetNameOfNode(ref container, true);
                    if (!group._updates.TryGetValue(name, out var node))
                    {
                        DateTime? lastWriteTime = null;
                        var info = new FileInfo(Path.Combine(group._root.FullName, name, name + "_update.mid"));
                        if (info.Exists)
                        {
                            lastWriteTime = AbridgedFileInfo.NormalizedLastWrite(info);
                        }
                        group._updates.Add(name, node = (new List<YARGTextContainer<byte>>(), lastWriteTime));
                    }
                    node.Containers.Add(container);
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

        public static void SerializeGroups(FileStream fileStream, List<CONUpdateGroup> groups)
        {
            using var groupStream = new MemoryStream();
            fileStream.Write(groups.Count, Endianness.Little);
            for (int i = 0; i < groups.Count; i++)
            {
                groupStream.SetLength(0);
                groups[i]._root.Serialize(groupStream);
                groupStream.Write(groups[i]._updates.Count, Endianness.Little);
                foreach (var node in groups[i]._updates)
                {
                    groupStream.Write(node.Key);
                    groupStream.Write(node.Value.Update.HasValue);
                    if (node.Value.Update.HasValue)
                    {
                        groupStream.Write(node.Value.Update.Value.ToBinary(), Endianness.Little);
                    }
                    groupStream.Write(groups[i].AssetSignature(node.Key));
                }
                fileStream.Write((int) groupStream.Length, Endianness.Little);
                fileStream.Write(groupStream.GetBuffer(), 0, (int) groupStream.Length);
            }
        }
    }
}
