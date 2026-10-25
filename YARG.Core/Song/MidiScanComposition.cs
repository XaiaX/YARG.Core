// pattern: Functional Core
using System;
using System.Collections.Generic;
using System.IO;
using Melanchall.DryWetMidi.Core;
using YARG.Core.Chart;
using YARG.Core.Extensions;
using YARG.Core.IO;

namespace YARG.Core.Song
{
    internal static class MidiScanComposition
    {
        internal static ScanExpected<long> Scan(FixedArray<byte> mainMidi,
            IReadOnlyList<FixedArray<byte>> updates, FixedArray<byte>? upgradeMidi,
            ref AvailableParts parts, out bool usedSummaries)
        {
            usedSummaries = false;
            var originalParts = parts;
            try
            {
                // Compilation observes every track, whereas the raw scanner may return earlier.
                // Any unsupported observation retries the entire original pipeline from clean state.
                if (TryScanSummaries(mainMidi, updates, upgradeMidi, ref parts, out var result))
                {
                    usedSummaries = true;
                    return result;
                }
            }
            catch (Exception error) when (!(error is OutOfMemoryException) && !(error is AccessViolationException))
            {
                // Summary support is an optimization, never a new reason to reject a source.
            }
            parts = originalParts;
            return ScanRaw(mainMidi, updates, upgradeMidi, ref parts);
        }

        private static bool TryScanSummaries(FixedArray<byte> mainMidi,
            IReadOnlyList<FixedArray<byte>> updates, FixedArray<byte>? upgradeMidi,
            ref AvailableParts parts, out ScanExpected<long> result)
        {
            result = default;
            if (!MidiSummaryEligibility.IsSupported(mainMidi.ReadOnlySpan)) return false;
            foreach (var bytes in updates)
                if (!MidiSummaryEligibility.IsSupported(bytes.ReadOnlySpan)) return false;
            if (upgradeMidi != null && !MidiSummaryEligibility.IsSupported(upgradeMidi.ReadOnlySpan)) return false;

            var summaries = new List<MidiScanSummary>();
            foreach (var bytes in updates) summaries.Add(MidiScanSummary.Compile(bytes));
            return TryScanSummaries(MidiScanSummary.Compile(mainMidi), summaries,
                upgradeMidi == null ? null : MidiScanSummary.Compile(upgradeMidi), ref parts, out result);
        }

        internal static bool TryScanSummaries(MidiScanSummary main, IReadOnlyList<MidiScanSummary> updates,
            MidiScanSummary? upgrade, ref AvailableParts parts, out ScanExpected<long> result)
        {
            result = default;
            var sourceParts = AvailableParts.Default;
            var sourceDrums = DrumsType.ProDrums;
            foreach (var summary in updates)
            {
                var status = summary.Scan(ref sourceParts, ref sourceDrums);
                if (status == MidiSummaryScanResult.Unsupported) return false;
                if (status != MidiSummaryScanResult.Success)
                {
                    result = new ScanUnexpected(status == MidiSummaryScanResult.InvalidResolution
                        ? ScanResult.InvalidResolution_Update : ScanResult.MultipleMidiTrackNames_Update);
                    return true;
                }
            }
            if (upgrade != null)
            {
                var status = upgrade.Scan(ref sourceParts, ref sourceDrums);
                if (status == MidiSummaryScanResult.Unsupported) return false;
                if (status != MidiSummaryScanResult.Success)
                {
                    result = new ScanUnexpected(status == MidiSummaryScanResult.InvalidResolution
                        ? ScanResult.InvalidResolution_Upgrade : ScanResult.MultipleMidiTrackNames_Upgrade);
                    return true;
                }
            }
            var baseStatus = main.Scan(ref sourceParts, ref sourceDrums);
            if (baseStatus == MidiSummaryScanResult.Unsupported) return false;
            if (baseStatus != MidiSummaryScanResult.Success)
            {
                result = new ScanUnexpected(baseStatus == MidiSummaryScanResult.InvalidResolution
                    ? ScanResult.InvalidResolution : ScanResult.MultipleMidiTrackNames);
                return true;
            }
            var effective = main;
            foreach (var summary in updates) effective = effective.Merge(summary);
            if (upgrade != null) effective = effective.Merge(upgrade);
            var drums = DrumsType.ProDrums;
            var finalStatus = effective.Scan(ref parts, ref drums);
            if (finalStatus == MidiSummaryScanResult.Unsupported) return false;
            result = finalStatus == MidiSummaryScanResult.Success ? new ScanExpected<long>(effective.Resolution)
                : new ScanUnexpected(finalStatus == MidiSummaryScanResult.InvalidResolution
                    ? ScanResult.InvalidResolution : ScanResult.MultipleMidiTrackNames);
            return true;
        }

        internal static ScanExpected<long> ScanRaw(FixedArray<byte> mainMidi,
            IReadOnlyList<FixedArray<byte>> updates, FixedArray<byte>? upgradeMidi, ref AvailableParts parts)
        {
            var sourceParts = AvailableParts.Default;
            var sourceDrumsType = DrumsType.ProDrums;
            foreach (var layerMidi in updates)
            {
                var result = SongEntry.ParseMidi(layerMidi, ref sourceParts, ref sourceDrumsType);
                switch (result.Error)
                {
                    case ScanResult.InvalidResolution:      return new ScanUnexpected(ScanResult.InvalidResolution_Update);
                    case ScanResult.MultipleMidiTrackNames: return new ScanUnexpected(ScanResult.MultipleMidiTrackNames_Update);
                }
            }

            if (upgradeMidi != null)
            {
                var upgradeResult = SongEntry.ParseMidi(upgradeMidi, ref sourceParts, ref sourceDrumsType);
                switch (upgradeResult.Error)
                {
                    case ScanResult.InvalidResolution:      return new ScanUnexpected(ScanResult.InvalidResolution_Upgrade);
                    case ScanResult.MultipleMidiTrackNames: return new ScanUnexpected(ScanResult.MultipleMidiTrackNames_Upgrade);
                }
            }

            var resolution = SongEntry.ParseMidi(mainMidi, ref sourceParts, ref sourceDrumsType);
            if (!resolution)
            {
                return resolution;
            }

            // LoadChart merges named tracks by replacement (base, updates, upgrade).
            // Parse the same effective track set rather than retaining the first active
            // part encountered in the separate source files.
            var effectiveMidi = MidiFile.Read(mainMidi.ToReferenceStream(), MidiSettingsLatin1.Instance);
            foreach (var layerMidi in updates)
            {
                effectiveMidi.Merge(MidiFile.Read(layerMidi.ToReferenceStream(), MidiSettingsLatin1.Instance), false);
            }
            if (upgradeMidi != null)
            {
                effectiveMidi.Merge(MidiFile.Read(upgradeMidi.ToReferenceStream(), MidiSettingsLatin1.Instance), false);
            }
            using var effectiveStream = new MemoryStream();
            effectiveMidi.Write(effectiveStream);
            using var effectiveBytes = FixedArray<byte>.Alloc((int) effectiveStream.Length);
            effectiveStream.GetBuffer().AsSpan(0, (int) effectiveStream.Length).CopyTo(effectiveBytes.Span);
            var drumsType = DrumsType.ProDrums;
            var effectiveResult = SongEntry.ParseMidi(effectiveBytes, ref parts, ref drumsType);
            if (!effectiveResult)
            {
                return effectiveResult;
            }

            return resolution;
        }
    }
}
