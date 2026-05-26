using System;
using System.Collections.Generic;
using System.Text;

namespace YARG.Core.Audio
{
    public class SerializedMic
    {
        public readonly string Name;

        /// <summary>
        /// Within-session unique ID (nullable for pre-StableId payloads).
        /// </summary>
        public string? StableId;

        public SerializedMic(string name)
        {
            Name = name;
            StableId = null;
        }

        public SerializedMic(string name, string stableId)
        {
            Name = name;
            StableId = stableId;
        }
    }
}
