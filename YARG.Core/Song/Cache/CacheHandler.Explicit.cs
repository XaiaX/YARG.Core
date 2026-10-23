using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using YARG.Core.IO;
using YARG.Core.Audio;
using YARG.Core.Chart;

namespace YARG.Core.Song.Cache
{
    public partial class CacheHandler
    {
        private const int EXPLICIT_VERSION = 2;
        private SongSourceConfiguration? explicitSources;
        private readonly List<BaseDescriptor> baseDescriptors = new();
        private HashSet<string>? rebuildNodes;
        private Inventory? appliedInventory;
        private readonly HashSet<string> excludedSupplementRoots = new(SongSourceConfiguration.PathComparer);
        internal static Action<string>? ScanObservation;
        internal static Action? BeforeCacheCommit;
        internal static Action<string>? BeforeTemporaryDelete;

        private sealed class Dependency
        {
            public string Path = string.Empty;
            public long Length;
            public DateTime Modified;
            public static Dependency Capture(string path)
            {
                var info = new FileInfo(path);
                return new Dependency { Path = path, Length = info.Exists ? info.Length : -1,
                    Modified = info.Exists ? AbridgedFileInfo.NormalizedLastWrite(info) : default };
            }
            public bool Valid() { var now = Capture(Path); return Length == now.Length && Modified == now.Modified; }
        }
        private sealed class SourceSnapshot
        {
            public string Path = string.Empty;
            public byte[] Data = Array.Empty<byte>();
            public List<string> Nodes = new();
            public int DefinitionCount;
            public List<Dependency> Dependencies = new();
            public Dictionary<string, string> MidiHashes = new();
            public Dictionary<string, List<string>> NodeAssets = new();
            public bool MidiContentValid() => MidiHashes.All(p => File.Exists(p.Key) && Fingerprint(File.ReadAllBytes(p.Key)) == p.Value);
            public bool ValidForNode(string node) => string.IsNullOrEmpty(Path) ||
                (File.Exists(Path) && Data.SequenceEqual(File.ReadAllBytes(Path)) && Dependencies.Where(d =>
                    (NodeAssets.TryGetValue(node, out var assets) && assets.Contains(d.Path)) ||
                    System.IO.Path.GetFileName(d.Path) == node + RBProUpgrade.UPGRADES_MIDI_EXT ||
                    d.Path.StartsWith(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, node) + System.IO.Path.DirectorySeparatorChar,
                        SongSourceConfiguration.PathComparer == StringComparer.OrdinalIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).All(d => d.Valid()));
            public bool Valid() => string.IsNullOrEmpty(Path) ||
                (File.Exists(Path) && Data.SequenceEqual(File.ReadAllBytes(Path)) && Dependencies.All(d => d.Valid()));
        }
        private sealed class BaseDescriptor
        {
            public string Root = string.Empty;
            public string Playlist = string.Empty;
            public CONEntryGroup.CONEntryType Type;
            public Dependency Identity = new();
            public List<string> Nodes = new();
            public Dictionary<string, List<Dependency>> Midi = new();
            public List<CONFileListing>? Listings;
        }
        private sealed class Inventory
        {
            public int Version = EXPLICIT_VERSION;
            public bool FullPlaylists;
            public string PayloadHash = string.Empty;
            public List<string> Roots = new();
            public List<BaseDescriptor> Bases = new();
            public List<string> ExcludedRoots = new();
            public SourceSnapshot Updates = new();
            public SourceSnapshot Upgrades = new();
        }
        private sealed class ExplicitEntryException : Exception
        {
            public ExplicitEntryException(string message) : base(message) { }
        }
        private sealed class FullScanRequiredException : Exception
        {
            public FullScanRequiredException(string message) : base(message) { }
        }

        private bool IsSupplementDirectory(string directory)
        {
            if (explicitSources == null) return false;
            if (excludedSupplementRoots.Contains(directory)) return true;
            return new[] { explicitSources.UpdatesDtaPath, explicitSources.UpgradesDtaPath }
                .Where(p => p.Length > 0).Any(p => SongSourceConfiguration.PathComparer.Equals(Path.GetDirectoryName(p), directory));
        }

        private void RecordDescriptor(CONEntryGroup group)
        {
            if (explicitSources == null || rebuildNodes != null) return;
            string identity = group.Type == CONEntryGroup.CONEntryType.PackedCONEntry ? group.Root.FullName : Path.Combine(group.Root.FullName, CONEntryGroup.SONGS_DTA);
            var descriptor = new BaseDescriptor { Root = group.Root.FullName,
                Type = group.Type, Playlist = group.DefaultPlaylist, Identity = Dependency.Capture(identity),
                Nodes = group.Select(n => n.Key).ToList(), Listings = (group as PackedCONEntryGroup)?.Listings };
            if (group.Type != CONEntryGroup.CONEntryType.PackedCONEntry)
                foreach (var node in group)
                {
                    var dependencies = new List<Dependency>();
                    foreach (var container in node.Value)
                    {
                        var dta = DTAEntry.Create(node.Key, container);
                        if (dta.Location == null) continue;
                        string relative = dta.Location.StartsWith("songs/", StringComparison.Ordinal) ? dta.Location.Substring(6) : dta.Location;
                        dependencies.Add(Dependency.Capture(Path.Combine(group.Root.FullName, relative +
                            (group.Type == CONEntryGroup.CONEntryType.UnpackedPKGEntry ? ".mid.edat" : ".mid"))));
                    }
                    descriptor.Midi[node.Key] = dependencies;
                }
            lock (baseDescriptors) baseDescriptors.Add(descriptor);
        }

        private SourceSnapshot LoadExplicitSource(string path, bool updates, SourceSnapshot? applied = null)
        {
            var snapshot = new SourceSnapshot { Path = path };
            if (path.Length == 0) return snapshot;
            if (!string.Equals(Path.GetExtension(path), ".dta", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Select a .dta file for " + (updates ? "Updates." : "Upgrades."));
            snapshot.Data = applied?.Data ?? File.ReadAllBytes(path);
            string definition = System.Text.Encoding.UTF8.GetString(snapshot.Data);
            string uncommented = string.Join("\n", definition.Split('\n').Select(line => line.Split(';')[0]));
            if (string.IsNullOrWhiteSpace(uncommented)) return snapshot;
            if (!uncommented.TrimStart().StartsWith("(", StringComparison.Ordinal)) throw new IOException("Invalid DTA definition: " + path);
            ValidateDefinition(definition, path);
            // Parsing is performed by the existing production DTA readers and entry constructors.
            if (updates)
            {
                if (!CONUpdateGroup.Create(Path.GetDirectoryName(path)!, new FileInfo(path), out var group, snapshot.Data))
                    throw new IOException("Cannot parse Updates DTA: " + path);
                group.ExplicitSource = true;
                updateGroups.Add(group);
                snapshot.Nodes.AddRange(group.Updates.Keys);
                snapshot.DefinitionCount = group.Updates.Values.Sum(node => node.Containers.Count);
                foreach (string node in snapshot.Nodes)
                {
                    string directory = Path.Combine(Path.GetDirectoryName(path)!, node);
                    snapshot.Dependencies.Add(Dependency.Capture(Path.Combine(directory, node + "_update.mid")));
                    snapshot.Dependencies.Add(Dependency.Capture(Path.Combine(directory, node + "_update.mogg")));
                    snapshot.Dependencies.Add(Dependency.Capture(Path.Combine(directory, node + ".voc")));
                    snapshot.Dependencies.Add(Dependency.Capture(Path.Combine(directory, "gen", node + "_keep.png_xbox")));
                    snapshot.Dependencies.Add(Dependency.Capture(Path.Combine(directory, "gen", node + ".milo_xbox")));
                    // Update definitions may supply only metadata, including disc_update nodes.
                    // Record absent assets too, so applied snapshots still detect later changes.
                }
            }
            else
            {
                var group = UnpackedCONUpgradeGroup.CreateExplicit(new FileInfo(path), snapshot.Data);
                unpackedUpgradeGroups.Add(group);
                // Include definitions without a MIDI, so removals and unmatched nodes remain observable.
                using var data = FixedArray<byte>.Alloc(snapshot.Data.Length);
                snapshot.Data.AsSpan().CopyTo(data.Span);
                var text = YARGDTAReader.Create(data);
                while (YARGDTAReader.StartNode(ref text))
                {
                    snapshot.Nodes.Add(YARGDTAReader.GetNameOfNode(ref text, true));
                    YARGDTAReader.EndNode(ref text);
                }
                snapshot.DefinitionCount = snapshot.Nodes.Count;
                foreach (string node in snapshot.Nodes)
                    snapshot.Dependencies.Add(Dependency.Capture(Path.Combine(Path.GetDirectoryName(path)!, node + RBProUpgrade.UPGRADES_MIDI_EXT)));
            }
            if (applied != null)
            {
                snapshot.Dependencies = applied.Dependencies;
                snapshot.MidiHashes = applied.MidiHashes;
                snapshot.NodeAssets = applied.NodeAssets;
            }
            else
            {
                ValidateSelectedAssets(snapshot);
                foreach (var dependency in snapshot.Dependencies.Where(d => d.Length >= 0 && d.Path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)))
                    snapshot.MidiHashes[dependency.Path] = Fingerprint(File.ReadAllBytes(dependency.Path));
            }
            if (!snapshot.Valid()) throw new IOException("Source changed while being read: " + path);
            return snapshot;
        }

        /// <summary>Runs an explicit-source transaction. Partial scopes never discover base songs.</summary>
        public static SongScanResult RunScan(SongScanScope scope, SongSourceConfiguration sources,
            bool tryQuickScan, string cacheLocation, string badSongsLocation, bool fullDirectoryPlaylists, bool deferCommit = false)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (!Enum.IsDefined(typeof(SongScanScope), scope)) throw new ArgumentOutOfRangeException(nameof(scope));
            bool partial = scope == SongScanScope.Updates || scope == SongScanScope.Upgrades;
            string? payload = null;
            string? candidate = null;
            GlobalAudioHandler.LogMixerStatus = false;
            try
            {
                Inventory? prior = null;
                byte[]? previousPayload = null;
                {
                    try
                    {
                        using var input = new BinaryReader(File.OpenRead(cacheLocation));
                        if (input.ReadString() != "YARG explicit sources" || input.ReadInt32() != EXPLICIT_VERSION)
                            throw new InvalidDataException();
                        prior = JsonConvert.DeserializeObject<Inventory>(input.ReadString());
                        int size = input.ReadInt32();
                        previousPayload = input.ReadBytes(size);
                        if (previousPayload.Length != size || input.BaseStream.Position != input.BaseStream.Length ||
                            prior == null || prior.PayloadHash != Fingerprint(previousPayload) || prior.Version != EXPLICIT_VERSION || prior.FullPlaylists != fullDirectoryPlaylists ||
                            !sources.SongFolders.SequenceEqual(prior.Roots, SongSourceConfiguration.PathComparer))
                            throw new InvalidDataException();
                    }
                    catch
                    {
                        if (partial) throw new FullScanRequiredException("A complete compatible song inventory is required. Run Scan All.");
                        prior = null;
                    }
                }
                if (partial && prior != null)
                {
                    var other = scope == SongScanScope.Updates ? prior.Upgrades : prior.Updates;
                    if (!other.Valid()) throw new IOException("Applied " + (scope == SongScanScope.Updates ? "Upgrades" : "Updates") +
                        " source changed or disappeared. Scan that section or Scan All before continuing.");
                }
                if (tryQuickScan && !partial && prior != null &&
                    SongSourceConfiguration.PathComparer.Equals(sources.UpdatesDtaPath, prior.Updates.Path) &&
                    SongSourceConfiguration.PathComparer.Equals(sources.UpgradesDtaPath, prior.Upgrades.Path) &&
                    prior.Updates.Valid() && prior.Upgrades.Valid() && prior.Updates.MidiContentValid() && prior.Upgrades.MidiContentValid())
                {
                    using var quick = new CacheHandler(sources.SongFolders.ToList(), false) { explicitSources = sources, appliedInventory = prior };
                    payload = TemporaryPath(cacheLocation);
                    File.WriteAllBytes(payload, previousPayload!);
                    using var data = LoadCacheToMemory(payload, fullDirectoryPlaylists, false);
                    if (data == null) throw new FullScanRequiredException("Invalid effective cache. Run Scan All.");
                    quick.Deserialize_Quick(data);
                    AttachValidation(quick.cache, prior);
                    return new SongScanResult(SongScanStatus.Success, quick.cache, string.Empty, sources,
                        updatesDefinitionCount: prior.Updates.DefinitionCount, upgradesDefinitionCount: prior.Upgrades.DefinitionCount);
                }
                var applied = partial && prior != null ? new SongSourceConfiguration(sources.SongFolders,
                    scope == SongScanScope.Updates ? sources.UpdatesDtaPath : prior.Updates.Path,
                    scope == SongScanScope.Upgrades ? sources.UpgradesDtaPath : prior.Upgrades.Path) : sources;
                using var handler = new CacheHandler(applied.SongFolders.ToList(), false) { explicitSources = applied, appliedInventory = prior };
                var next = new Inventory { Roots = applied.SongFolders.ToList(), FullPlaylists = fullDirectoryPlaylists };
                if (prior != null) next.ExcludedRoots.AddRange(prior.ExcludedRoots);
                foreach (string path in new[] { applied.UpdatesDtaPath, applied.UpgradesDtaPath })
                    if (path.Length > 0 && !next.ExcludedRoots.Contains(Path.GetDirectoryName(path)!)) next.ExcludedRoots.Add(Path.GetDirectoryName(path)!);
                handler.excludedSupplementRoots.UnionWith(next.ExcludedRoots);
                next.Updates = handler.LoadExplicitSource(applied.UpdatesDtaPath, true,
                    partial && scope == SongScanScope.Upgrades ? prior!.Updates : null);
                next.Upgrades = handler.LoadExplicitSource(applied.UpgradesDtaPath, false,
                    partial && scope == SongScanScope.Updates ? prior!.Upgrades : null);
                int affected = 0;
                if (partial && prior != null)
                {
                    handler.rebuildNodes = new HashSet<string>((scope == SongScanScope.Updates ? prior.Updates.Nodes : prior.Upgrades.Nodes)
                        .Concat(scope == SongScanScope.Updates ? next.Updates.Nodes : next.Upgrades.Nodes));
                    payload = TemporaryPath(cacheLocation);
                    File.WriteAllBytes(payload, previousPayload!);
                    using var data = LoadCacheToMemory(payload, fullDirectoryPlaylists, false);
                    if (data == null) throw new FullScanRequiredException("Invalid effective cache. Run Scan All.");
                    handler.Deserialize_Quick(data);
                    // Modifications are constructed lazily from the effective source groups, never cached metadata.
                    var retainedGroups = handler.conEntryGroups.ToArray();
                    handler.conEntryGroups.Clear();
                    foreach (var descriptor in prior.Bases)
                    {
                        var cached = retainedGroups.FirstOrDefault(g => SongSourceConfiguration.PathComparer.Equals(g.Root.FullName, descriptor.Root));
                        if (!descriptor.Nodes.Any(handler.rebuildNodes.Contains))
                        {
                            if (cached != null) handler.conEntryGroups.Add(cached);
                            continue;
                        }
                        if (!descriptor.Identity.Valid() || descriptor.Midi.Where(p => handler.rebuildNodes.Contains(p.Key)).SelectMany(p => p.Value).Any(d => !d.Valid())) throw new FullScanRequiredException("An affected base package changed or disappeared. Run Scan All.");
                        ScanObservation?.Invoke("base:" + descriptor.Root);
                        CONEntryGroup? group;
                        if (descriptor.Type == CONEntryGroup.CONEntryType.PackedCONEntry)
                            group = handler.CreateCONGroup(new FileInfo(descriptor.Root), descriptor.Playlist).Entries;
                        else
                        {
                            bool ok = descriptor.Type == CONEntryGroup.CONEntryType.UnpackedPKGEntry
                                ? UnpackedPKGEntryGroup.Create(descriptor.Root, new FileInfo(descriptor.Identity.Path), descriptor.Playlist, out group!)
                                : UnpackedCONEntryGroup.Create(descriptor.Root, new FileInfo(descriptor.Identity.Path), descriptor.Playlist, out group!);
                            if (ok) handler.conEntryGroups.Add(group!);
                        }
                        if (group == null) throw new FullScanRequiredException("An affected base package cannot be opened. Run Scan All.");
                        cached?.CopyEntriesTo(group, handler.rebuildNodes);
                        affected += group.Where(n => handler.rebuildNodes.Contains(n.Key)).Sum(n => n.Value.Count);
                    }
                    handler.cache.Entries.Clear();
                    foreach (var ini in handler.iniGroups) foreach (var entry in ini.CachedEntries) handler.AddEntry(entry);
                    foreach (var group in handler.conEntryGroups) foreach (var entry in group.EffectiveEntries) handler.AddEntry(entry);
                    handler.BuildCONEntries();
                    next.Bases = prior.Bases;
                }
                else
                {
                    handler.FindNewEntries(fullDirectoryPlaylists);
                    next.Bases = handler.baseDescriptors;
                    affected = next.Bases.Sum(b => b.Nodes.Count);
                }
                CompleteUpdateAssetManifest(next.Updates, handler.cache, next.Bases, scope == SongScanScope.Upgrades);
                if (!next.Updates.Valid() || !next.Upgrades.Valid()) throw new IOException("Supplemental files changed during scanning. Scan the changed section again.");
                if (partial && next.Bases.Where(b => b.Nodes.Any(handler.rebuildNodes!.Contains)).Any(b => !b.Identity.Valid() ||
                    b.Midi.Where(p => handler.rebuildNodes!.Contains(p.Key)).SelectMany(p => p.Value).Any(d => !d.Valid())))
                    throw new FullScanRequiredException("An affected base package changed during scanning. Run Scan All.");
                payload ??= TemporaryPath(cacheLocation);
                handler.Serialize(payload, fullDirectoryPlaylists);
                byte[] serializedPayload = File.ReadAllBytes(payload);
                next.PayloadHash = Fingerprint(serializedPayload);
                candidate = TemporaryPath(cacheLocation);
                using (var output = new BinaryWriter(File.Open(candidate, FileMode.CreateNew, FileAccess.Write)))
                {
                    output.Write("YARG explicit sources");
                    output.Write(EXPLICIT_VERSION);
                    output.Write(JsonConvert.SerializeObject(next));
                    output.Write(serializedPayload.Length);
                    output.Write(serializedPayload);
                    output.Flush();
                    ((FileStream)output.BaseStream).Flush(true);
                }
                var affectedNodes = handler.rebuildNodes;
                var commitBases = next.Bases.Where(b => !partial || b.Nodes.Any(affectedNodes!.Contains)).ToArray();
                string retainedCandidate = candidate;
                var diagnosticEntries = handler.badSongs.ToArray();
                string? diagnosticText = diagnosticEntries.Length == 0 ? null :
                    string.Join(Environment.NewLine, diagnosticEntries.Select(p => p.Key + ": " + p.Value));
                AttachValidation(handler.cache, next);
                var result = new SongScanResult(SongScanStatus.Success, handler.cache, string.Empty, applied, affected,
                    next.Updates.Nodes.Concat(next.Upgrades.Nodes).Distinct().Count(n => !next.Bases.Any(b => b.Nodes.Contains(n))),
                    next.Updates.DefinitionCount, next.Upgrades.DefinitionCount);
                result.SetCandidate(() =>
                {
                    BeforeCacheCommit?.Invoke();
                    if (commitBases.Any(b => !b.Identity.Valid() ||
                        b.Midi.Where(p => !partial || affectedNodes!.Contains(p.Key)).SelectMany(p => p.Value).Any(d => !d.Valid())))
                        throw new IOException("An affected base package changed before publication. Run Scan All.");
                    if ((scope != SongScanScope.Upgrades && !next.Updates.MidiContentValid()) ||
                        (scope != SongScanScope.Updates && !next.Upgrades.MidiContentValid()))
                        throw new IOException("Selected source MIDI changed during scanning. Scan again.");
                    if (!next.Updates.Valid() || !next.Upgrades.Valid()) throw new IOException("Supplemental files changed before publication. Scan again.");
                    if (File.Exists(cacheLocation)) File.Replace(retainedCandidate, cacheLocation, null);
                    else File.Move(retainedCandidate, cacheLocation);
                    WriteExplicitDiagnostics(badSongsLocation, diagnosticText);
                }, () => CleanupTemporary(retainedCandidate));
                try
                {
                    if (!deferCommit) result.Commit();
                }
                catch { result.Dispose(); throw; }
                candidate = null;
                return result;
            }
            catch (AggregateException ex) when (ex.Flatten().InnerExceptions.Any(e => e is FullScanRequiredException))
            {
                string message = ex.Flatten().InnerExceptions.First(e => e is FullScanRequiredException).Message;
                WriteExplicitDiagnostics(badSongsLocation, message);
                return new SongScanResult(SongScanStatus.NeedsFullScan, null, message);
            }
            catch (FullScanRequiredException ex)
            {
                WriteExplicitDiagnostics(badSongsLocation, ex.Message);
                return new SongScanResult(SongScanStatus.NeedsFullScan, null, ex.Message);
            }
            catch (Exception ex)
            {
                string message = ex is AggregateException aggregate ?
                    string.Join("; ", aggregate.Flatten().InnerExceptions.Select(e => e.Message).Distinct()) : ex.Message;
                WriteExplicitDiagnostics(badSongsLocation, message);
                return new SongScanResult(SongScanStatus.SourceError, null, message);
            }
            finally
            {
                GlobalAudioHandler.LogMixerStatus = true;
                if (payload != null) CleanupTemporary(payload);
                if (candidate != null) CleanupTemporary(candidate);
            }
        }
        private static void CompleteUpdateAssetManifest(SourceSnapshot source, SongCache cache, List<BaseDescriptor> bases, bool validateAppliedPathsOnly)
        {
            if (source.Path.Length == 0) return;
            foreach (string node in source.Nodes)
            {
                var subnames = new HashSet<string>(cache.Entries.Values.SelectMany(v => v).OfType<RBCONEntry>()
                    .Where(e => e.NodeName == node).Select(e => e.AssetSubName), StringComparer.Ordinal);
                foreach (var descriptor in bases)
                    if (descriptor.Midi.TryGetValue(node, out var midis))
                        foreach (var midi in midis) subnames.Add(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(midi.Path)!));
                var paths = new List<string>();
                foreach (string subname in subnames)
                {
                    string directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source.Path)!, subname);
                    paths.Add(System.IO.Path.Combine(directory, subname + "_update.mogg"));
                    paths.Add(System.IO.Path.Combine(directory, subname + ".voc"));
                    paths.Add(System.IO.Path.Combine(directory, "gen", subname + "_keep.png_xbox"));
                    paths.Add(System.IO.Path.Combine(directory, "gen", subname + ".milo_xbox"));
                }
                if (validateAppliedPathsOnly)
                {
                    if (paths.Any(path => !source.NodeAssets.TryGetValue(node, out var appliedPaths) ||
                        !appliedPaths.Contains(path) || !source.Dependencies.Any(d => SongSourceConfiguration.PathComparer.Equals(d.Path, path))))
                        throw new IOException("Upgrades changes runtime asset paths used by applied Updates. Run Scan Updates or Scan All before applying this location change.");
                    continue;
                }
                source.NodeAssets[node] = paths;
                foreach (string path in paths)
                    if (!source.Dependencies.Any(d => SongSourceConfiguration.PathComparer.Equals(d.Path, path)))
                        source.Dependencies.Add(Dependency.Capture(path));
            }
            if (!validateAppliedPathsOnly) ValidateSelectedAssets(source);
        }

        internal static void CleanupTemporary(string path)
        {
            try
            {
                BeforeTemporaryDelete?.Invoke(path);
                File.Delete(path);
            }
            catch (Exception ex) { YARG.Core.Logging.YargLogger.LogException(ex, "Could not delete temporary scan file: " + path); }
        }

        private static bool IsSupplementalError(ScanResult error) => error == ScanResult.MoggError_Update ||
            error == ScanResult.InvalidResolution_Update || error == ScanResult.InvalidResolution_Upgrade ||
            error == ScanResult.MultipleMidiTrackNames_Update || error == ScanResult.MultipleMidiTrackNames_Upgrade;

        private static void ValidateSelectedAssets(SourceSnapshot source)
        {
            foreach (var dependency in source.Dependencies.Where(d => d.Length >= 0))
            {
                try
                {
                    if (dependency.Path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
                    {
                        using var data = FixedArray.LoadFile(dependency.Path);
                        var parts = AvailableParts.Default;
                        var drums = DrumsType.ProDrums;
                        var result = SongEntry.ParseMidi(data, ref parts, ref drums);
                        if (!result) throw new InvalidDataException(result.Error.ToString());
                        using var stream = data.ToReferenceStream();
                        Melanchall.DryWetMidi.Core.MidiFile.Read(stream);
                    }
                    else if (dependency.Path.EndsWith(".mogg", StringComparison.OrdinalIgnoreCase))
                    {
                        using var stream = File.OpenRead(dependency.Path);
                        var result = RBCONEntry.ValidateMoggHeader(stream);
                        if (result != ScanResult.Success) throw new InvalidDataException(result.ToString());
                    }
                }
                catch (Exception ex) { throw new IOException("Invalid selected supplemental asset " + dependency.Path + ": " + ex.Message, ex); }
            }
        }

        private static void WriteExplicitDiagnostics(string location, string? text)
        {
            string? temporary = null;
            try
            {
                if (text == null) { File.Delete(location); return; }
                temporary = TemporaryPath(location);
                File.WriteAllText(temporary, text + Environment.NewLine);
                if (File.Exists(location)) File.Replace(temporary, location, null);
                else File.Move(temporary, location);
            }
            catch (Exception ex) { YARG.Core.Logging.YargLogger.LogException(ex, "Could not write scan diagnostics"); }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) { YARG.Core.Logging.YargLogger.LogException(ex); }
                }
            }
        }

        private static void ValidateDefinition(string text, string path)
        {
            int depth = 0;
            bool quoted = false, escaped = false, comment = false;
            foreach (char c in text)
            {
                if (comment) { if (c == '\n' || c == '\r') comment = false; continue; }
                if (escaped) { escaped = false; continue; }
                if (quoted && c == '\\') { escaped = true; continue; }
                if (c == '"') { quoted = !quoted; continue; }
                if (quoted) continue;
                if (c == ';') { comment = true; continue; }
                if (c == '(') depth++;
                else if (c == ')' && --depth < 0) throw new IOException("Unbalanced DTA definition: " + path);
            }
            if (quoted || depth != 0) throw new IOException("Truncated DTA definition: " + path);
        }

        private static void AttachValidation(SongCache cache, Inventory inventory)
        {
            foreach (var entry in cache.Entries.Values.SelectMany(v => v).OfType<RBCONEntry>())
            {
                var relevant = new[] { inventory.Updates, inventory.Upgrades }.Where(s => s.Nodes.Contains(entry.NodeName)).ToArray();
                entry.SupplementalValidation = () =>
                {
                    try { return relevant.All(s => s.ValidForNode(entry.NodeName)); }
                    catch { return false; }
                };
            }
        }

        private static string Fingerprint(byte[] data)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data));
        }

        private static string TemporaryPath(string cacheLocation) => cacheLocation + "." + Guid.NewGuid().ToString("N") + ".tmp";
    }
}
