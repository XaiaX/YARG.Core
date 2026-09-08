using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace YARG.Core.Audio
{
    public class SerializedMic
    {
        public readonly string BaseName;
        public readonly int Channel;

        /// <summary>
        /// Within-session unique ID (nullable for pre-StableId payloads).
        /// See <see cref="MicDevice.StableId"/> / <see cref="MicDevice.ComputeStableId"/>.
        /// </summary>
        public string? StableId;

        public SerializedMic(string baseName, int channel)
        {
            BaseName = baseName;
            Channel = channel;
            StableId = null;
        }

        public SerializedMic(string displayName)
        {
            if (InputDeviceInfo.TryParseDisplayName(displayName, out var parsedBaseName, out var parsedChannel))
            {
                BaseName = parsedBaseName;
                Channel = parsedChannel;
            }
            else
            {
                BaseName = displayName ?? string.Empty;
                Channel = 0;
            }
            StableId = null;
        }

        [JsonConstructor]
        public SerializedMic(string? baseName, int channel, string? stableId, string? name = null)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                if (!string.IsNullOrWhiteSpace(name) && InputDeviceInfo.TryParseDisplayName(name, out var parsedBaseName, out var parsedChannel))
                {
                    baseName = parsedBaseName;
                    channel = parsedChannel;
                }
                else
                {
                    throw new ArgumentException("Serialized microphone is missing a valid identity", nameof(baseName));
                }
            }

            BaseName = baseName;
            Channel = channel;
            StableId = stableId;
        }

        public string Name => Channel > 0 ? $"{BaseName} - Channel {Channel + 1}" : BaseName;
    }
}
