// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YARG.Core.IO;
using YARG.Core.Logging;
using YARG.Core.Song.Cache;
using YARG.Core.Song.Supplements;

namespace YARG.Core.Song
{
    public abstract partial class RBCONEntry
    {
        private sealed class ScanInput : IDisposable
        {
            private readonly Func<FixedArray<byte>> _load;
            private readonly bool _ownsBytes;
            private readonly PackageLease? _package;
            private FixedArray<byte>? _bytes;
            private readonly MidiScanSummary? _summary;
            private MidiContentFingerprint? _fingerprint;

            internal ScanInput(FixedArray<byte> bytes)
            { _bytes = bytes; _load = () => bytes; }

            internal ScanInput(Func<FixedArray<byte>> load)
            { _load = load; _ownsBytes = true; }

            internal ScanInput(PackageLease package, string relative)
            {
                _package = package;
                if (!package.Package.TryGetAsset(relative, out var asset))
                    throw new SupplementalPackageException("Package MIDI missing: " + relative);
                _summary = asset.Summary;
                _fingerprint = new MidiContentFingerprint(asset.Sha256, asset.Length);
                _load = () => SupplementalAssetSource.Read(package.Package, relative);
                _ownsBytes = true;
            }

            internal FixedArray<byte> Bytes => _bytes ??= _load();
            internal MidiContentFingerprint Fingerprint => _fingerprint ??= MidiContentFingerprint.Compute(Bytes.ReadOnlySpan);
            internal MidiScanSummary? Summary
            {
                get
                {
                    if (_summary != null) return _summary;
                    try
                    {
                        return MidiSummaryEligibility.IsSupported(Bytes.ReadOnlySpan) ? MidiScanSummary.Compile(Bytes) : null;
                    }
                    catch (SupplementalPackageException) { throw; }
                    catch (Exception error) when (!(error is OutOfMemoryException) && !(error is AccessViolationException)) { return null; }
                }
            }
            public void Dispose() { if (_ownsBytes) _bytes?.Dispose(); _package?.Dispose(); }
        }

        private ScanInput CreateScanInput(string dtaPath, string relative)
        {
            if (!SupplementalAssetSource.IsPackage(dtaPath)) return new ScanInput(() => LoadSupplementAsset(dtaPath, relative));
            var package = OpenSupplementPackage(dtaPath);
            try { return new ScanInput(package, relative.Replace('\\', '/')); }
            catch { package.Dispose(); throw; }
        }

        private static ScanResult ScanPackagedMidis(RBCONEntry entry, FixedArray<byte> mainMidi, LegacySongHashCache? hashCache)
        {
            using var main = new ScanInput(mainMidi);
            var updates = new List<ScanInput>();
            ScanInput? upgrade = null;
            try
            {
                foreach (var layer in entry._updateLayers)
                {
                    if (!AbridgedFileInfo.Validate(layer.DtaPath, layer.Root.LastWriteTime))
                        throw new SupplementalPackageException("Supplemental source changed during scanning.");
                    if (layer.Midi.HasValue)
                    {
                        string relative = entry._nodeName + "/" + entry._nodeName + "_update.mid";
                        if (!SupplementalAssetSource.IsPackage(layer.DtaPath) &&
                            !AbridgedFileInfo.Validate(Path.Combine(layer.Root.FullName, relative), layer.Midi.Value))
                            return ScanResult.PossibleCorruption;
                        updates.Add(entry.CreateScanInput(layer.DtaPath, relative));
                    }
                }
                if (entry._updateLayers.Length == 0 && entry._updateMidiLastWrite.HasValue)
                {
                    string legacy = Path.Combine(entry._updateDirectoryAndDtaLastWrite!.Value.FullName, entry._nodeName, entry._nodeName + "_update.mid");
                    updates.Add(new ScanInput(() => FixedArray.LoadFile(legacy)));
                }
                if (entry._upgrade is UnpackedRBProUpgrade unpacked && SupplementalAssetSource.IsPackage(unpacked.DtaPath))
                    upgrade = entry.CreateScanInput(unpacked.DtaPath, unpacked.RelativePath);
                else if (entry._upgrade != null)
                    upgrade = new ScanInput(() => entry._upgrade.LoadUpgradeMidi() ?? throw new FileNotFoundException("Upgrade MIDI missing."));

                var initialParts = entry._parts;
                ScanExpected<long> resolution = default;
                bool usedSummaries = false;
                try
                {
                    // Validate supplements in the same order as the loose scanner.
                    var updateSummaries = updates.Select(input => input.Summary).ToArray();
                    var upgradeSummary = upgrade?.Summary;
                    var baseSummary = main.Summary;
                    if (baseSummary != null && updateSummaries.All(summary => summary != null) && (upgrade == null || upgradeSummary != null))
                        usedSummaries = MidiScanComposition.TryScanSummaries(baseSummary, updateSummaries!, upgradeSummary,
                            ref entry._parts, out resolution);
                }
                catch (SupplementalPackageException) { throw; }
                catch (Exception error) when (!(error is OutOfMemoryException) && !(error is AccessViolationException)) { }
                if (!usedSummaries)
                {
                    entry._parts = initialParts;
                    resolution = MidiScanComposition.ScanRaw(main.Bytes, updates.Select(input => input.Bytes).ToArray(), upgrade?.Bytes, ref entry._parts);
                }
                CacheHandler.ScanObservation?.Invoke((usedSummaries ? "scan-summary:" : "scan-fallback:") + entry._nodeName);
                if (!resolution) return resolution.Error;
                if (!IsValid(in entry._parts)) return ScanResult.NoNotes;
                entry._parts.ProDrums.Difficulties = entry._parts.FourLaneDrums.Difficulties;
                entry._settings.SustainCutoffThreshold = resolution.Value / 3;
                if (entry._settings.HopoThreshold == -1) entry._settings.HopoThreshold = entry._settings.SustainCutoffThreshold;

                HashWrapper ComputeHash()
                {
                    CacheHandler.ScanObservation?.Invoke("legacy-hash:" + entry._nodeName);
                    var originals = new List<FixedArray<byte>> { main.Bytes };
                    originals.AddRange(updates.Select(input => input.Bytes));
                    if (upgrade != null) originals.Add(upgrade.Bytes);
                    return LegacySongHashCache.HashOriginalInputs(originals);
                }
                var vector = new MidiInputVector(main.Fingerprint, updates.Select(input => input.Fingerprint), upgrade?.Fingerprint);
                entry._hash = hashCache == null ? ComputeHash() : hashCache.GetOrAdd(vector, ComputeHash);
                return ScanResult.Success;
            }
            catch (SupplementalPackageException) { throw; }
            catch (Exception error) { YargLogger.LogException(error); return ScanResult.PossibleCorruption; }
            finally { foreach (var update in updates) update.Dispose(); upgrade?.Dispose(); }
        }
    }
}
