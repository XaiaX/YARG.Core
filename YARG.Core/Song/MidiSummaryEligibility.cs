// pattern: Functional Core
using System;
using System.Buffers.Binary;

namespace YARG.Core.Song
{
    /// <summary>
    /// Conservative eligibility check, not a MIDI validator. Noncanonical encodings and extensions
    /// go through the existing parser, including its repair policies, rather than changing acceptance.
    /// All tracks are bounded here, even when their names are not recognized by the preparsers.
    /// </summary>
    internal static class MidiSummaryEligibility
    {
        internal static bool IsSupported(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 14 || !Tag(bytes, "MThd") || Read32(bytes, 4) != 6) return false;
            int format = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(8));
            int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(10));
            int division = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(12));
            if (format > 1 || count == 0 || (format == 0 && count != 1) || division >= 0x8000) return false;
            int offset = 14;
            for (int i = 0; i < count; i++)
            {
                if (bytes.Length - offset < 8 || !Tag(bytes.Slice(offset), "MTrk")) return false;
                uint length = Read32(bytes, offset + 4);
                offset += 8;
                if (length > bytes.Length - offset || !Track(bytes.Slice(offset, (int)length))) return false;
                offset += (int)length;
            }
            return offset == bytes.Length;
        }

        private static bool Track(ReadOnlySpan<byte> bytes)
        {
            int offset = 0;
            int runningStatus = 0;
            ulong ticks = 0;
            while (offset < bytes.Length)
            {
                if (!Variable(bytes, ref offset, out int delta)) return false;
                ticks += (uint)delta;
                if (ticks > uint.MaxValue || offset == bytes.Length) return false;
                int status = bytes[offset];
                if (status >= 0x80) offset++;
                else status = runningStatus;
                if (status >= 0x80 && status < 0xf0)
                {
                    runningStatus = status;
                    int size = (status & 0xe0) == 0xc0 ? 1 : 2;
                    if (bytes.Length - offset < size) return false;
                    for (int i = 0; i < size; i++) if (bytes[offset++] >= 0x80) return false;
                }
                else if (status == 0xff)
                {
                    runningStatus = 0;
                    if (offset == bytes.Length) return false;
                    int type = bytes[offset++];
                    if (!Variable(bytes, ref offset, out int size) || size > bytes.Length - offset) return false;
                    if (type == 0x2f) return size == 0 && offset == bytes.Length;
                    // Text is byte-preserving with Latin-1. Restrict other events to simple canonical
                    // tempo/time-signature forms; everything else retains the raw parser's policies.
                    if (type >= 1 && type <= 9) { }
                    else if (type == 0x51 && size == 3 && (bytes[offset] | bytes[offset + 1] | bytes[offset + 2]) != 0) { }
                    else if (type == 0x58 && size == 4 && bytes[offset] > 0 && bytes[offset + 1] <= 7) { }
                    else return false;
                    offset += size;
                }
                else return false;
            }
            return false; // Require one final, zero-length end-of-track event.
        }

        private static bool Variable(ReadOnlySpan<byte> bytes, ref int offset, out int value)
        {
            value = 0;
            for (int i = 0; i < 4 && offset < bytes.Length; i++)
            {
                byte next = bytes[offset++];
                if (i == 0 && next == 0x80) return false;
                value = (value << 7) | (next & 0x7f);
                if (next < 0x80) return true;
            }
            return false;
        }

        private static uint Read32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset));
        private static bool Tag(ReadOnlySpan<byte> bytes, string tag) => bytes.Length >= 4 &&
            bytes[0] == tag[0] && bytes[1] == tag[1] && bytes[2] == tag[2] && bytes[3] == tag[3];
    }
}
