// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using YARG.Core.IO;

namespace YARG.Core.Song.Supplements
{
    public static class SupplementPackageBuilder
    {
        public static void Pack(string dtaPath, SupplementSourceKind kind, string outputPath)
        {
            if (!Enum.IsDefined(typeof(SupplementSourceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            string output = Path.GetFullPath(outputPath);
            if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Package output already exists: " + output);
            string source = Path.GetFullPath(dtaPath);
            byte[] dta = ReadFile(source, SupplementPackage.MAX_DTA_BYTES);
            var definitions = ReadDefinitions(dta);
            string root = Path.GetDirectoryName(source)!;
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                string node = definition.Node;
                SupplementPaths.Validate(node);
                string midi = kind == SupplementSourceKind.Updates ? node + "/" + node + "_update.mid" : node + "_plus.mid";
                if (File.Exists(Path.Combine(root, midi))) paths.Add(midi);
                else if (kind == SupplementSourceKind.Upgrades) throw new FileNotFoundException("Upgrade MIDI missing: " + midi);
            }
            if (kind == SupplementSourceKind.Updates)
            {
                // Auxiliary paths are selected using the BASE song's asset name at runtime. That
                // name need not equal any update node, so retain all standard optional assets.
                foreach (string file in EnumerateFiles(root))
                {
                    string extension = Path.GetExtension(file);
                    if (extension.Equals(".mogg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".voc", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".png_xbox", StringComparison.OrdinalIgnoreCase) || extension.Equals(".milo_xbox", StringComparison.OrdinalIgnoreCase))
                        paths.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
            if (paths.Count > SupplementPackage.MAX_ENTRIES) throw new InvalidDataException("Too many package assets.");
            var manifest = new PackageManifest { Kind = kind == SupplementSourceKind.Updates ? "updates" : "upgrades", DtaPath = Path.GetFileName(source),
                DtaLength = dta.Length, DtaSha256 = SupplementPackage.Hash(dta), Definitions = definitions.Select(d => d.Node).ToList() };
            SupplementPaths.Validate(manifest.DtaPath);
            string staging = output + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    WriteEntry(archive, "sources/original.dta", dta);
                    var blobs = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string relative in paths)
                    {
                        SupplementPaths.Validate(relative);
                        byte[] bytes = ReadFile(Path.Combine(root, relative), SupplementPackage.MAX_ASSET_BYTES);
                        string digest = SupplementPackage.Hash(bytes);
                        var record = new AssetRecord { Path = relative, Sha256 = digest, Length = bytes.Length };
                        if (relative.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) && MidiSummaryEligibility.IsSupported(bytes))
                        {
                            try
                            {
                                using var buffer = FixedArray<byte>.Alloc(bytes.Length); bytes.AsSpan().CopyTo(buffer.Span);
                                record.Summary = Convert.ToBase64String(MidiScanSummary.Compile(buffer).Serialize());
                            }
                            catch (Exception e) when (!(e is OutOfMemoryException)) { /* Preserve raw parser fallback. */ }
                        }
                        manifest.Assets.Add(record);
                        if (blobs.Add(digest)) WriteEntry(archive, "blobs/sha256/" + digest, bytes);
                    }
                    byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest, Formatting.None));
                    if (json.Length > SupplementPackage.MAX_MANIFEST_BYTES) throw new InvalidDataException("Package manifest too large.");
                    WriteEntry(archive, "manifest.json", json);
                }
                using (var package = SupplementPackage.Open(staging)) package.Verify();
                File.Move(staging, output);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
        private static IEnumerable<string> EnumerateFiles(string directory)
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links are not package assets: " + file);
                yield return file;
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links are not package directories: " + child);
                foreach (string file in EnumerateFiles(child)) yield return file;
            }
        }
        internal static List<(string Node, string? Location)> ReadDefinitions(byte[] dta)
        {
            try { return ParseDefinitions(dta); }
            catch (Exception error) when (!(error is OutOfMemoryException) && !(error is InvalidDataException))
            { throw new InvalidDataException("Invalid supplemental DTA.", error); }
        }
        private static List<(string Node, string? Location)> ParseDefinitions(byte[] dta)
        {
            ValidateDelimiters(dta);
            var result = new List<(string, string?)>();
            // The production reader requires at least three bytes for encoding detection.
            using var buffer = FixedArray<byte>.Alloc(Math.Max(3, dta.Length));
            buffer.Span.Clear(); dta.AsSpan().CopyTo(buffer.Span);
            var text = YARGDTAReader.Create(buffer);
            YARGDTAReader.SkipWhitespace(ref text);
            while (YARGDTAReader.StartNode(ref text))
            {
                string node = YARGDTAReader.GetNameOfNode(ref text, true);
                var entry = DTAEntry.Create(node, text);
                result.Add((node, entry.Location));
                if (result.Count > SupplementPackage.MAX_ENTRIES) throw new InvalidDataException("Too many source definitions.");
                YARGDTAReader.EndNode(ref text);
            }
            // Reject non-DTA garbage rather than silently publishing an empty package.
            YARGDTAReader.SkipWhitespace(ref text);
            if (!text.IsAtEnd() && text.Get() != 0) throw new InvalidDataException("Invalid supplemental DTA.");
            return result;
        }
        // Structural guard only: all node names and metadata still use the production DTA reader.
        // EndNode tolerates an EOF before its matching close, which cannot be accepted for packaging.
        private static void ValidateDelimiters(byte[] bytes)
        {
            int depth = 0, braces = 0;
            byte quote = 0;
            bool comment = false, escape = false;
            foreach (byte c in bytes)
            {
                if (comment) { if (c == 10 || c == 13) comment = false; continue; }
                if (escape) { escape = false; continue; }
                if (quote != 0)
                {
                    if (c == 92) escape = true;
                    else if (c == quote) quote = 0;
                    continue;
                }
                if (braces > 0)
                {
                    if (c == '{') braces++;
                    else if (c == '}') braces--;
                    continue;
                }
                if (c == ';') comment = true;
                else if (c == 34 || c == 39) quote = c;
                else if (c == '{') braces++;
                else if (c == '(') depth++;
                else if (c == ')' && --depth < 0) throw new InvalidDataException("Unbalanced supplemental DTA.");
            }
            if (depth != 0 || braces != 0 || quote != 0) throw new InvalidDataException("Truncated supplemental DTA.");
        }
        private static byte[] ReadFile(string path, long limit)
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > limit || stream.Length > int.MaxValue) throw new InvalidDataException("Supplement asset exceeds size limit: " + path);
            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new IOException("Source changed during packing: " + path);
                offset += count;
            }
            if (stream.ReadByte() >= 0) throw new IOException("Source changed during packing: " + path);
            return bytes;
        }
        private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open(); stream.Write(bytes, 0, bytes.Length);
        }
    }
}
