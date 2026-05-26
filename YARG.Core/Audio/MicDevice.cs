using System;
using System.Collections.Generic;
using System.Text;

namespace YARG.Core.Audio
{
    public readonly struct MicOutputFrame
    {
        /// <summary>
        /// The time of the input. This is NOT relative!
        /// </summary>
        public readonly double Time;

        /// <summary>
        /// Whether or not this output frame is a mic hit, or a sing output.
        /// </summary>
        public readonly bool IsHit;

        /// <summary>
        /// Pitch (in hertz) of the microphone.
        /// </summary>
        public readonly float Pitch;

        /// <summary>
        /// Volume (in dB) of the microphone.
        /// </summary>
        public readonly float Volume;

        /// <summary>
        /// Gets the pitch as a MIDI note.
        /// </summary>
        public float PitchAsMidiNote => 12f * MathF.Log(Pitch / 440f, 2f) + 69f;

        public MicOutputFrame(double time, bool isHit, float pitch, float volume)
        {
            Time = time;
            IsHit = isHit;
            Pitch = pitch;
            Volume = volume;
        }
    }

    public abstract class MicDevice : IDisposable
    {
        public const int RECORD_PERIOD_MS = 40;
        public const float UPDATES_PER_SECOND = 1000f / RECORD_PERIOD_MS;

        private bool _disposed;

        public readonly string DisplayName;
        public bool IsMonitoring;
        public bool IsRecordingOutput;

        /// <summary>
        /// A within-session-unique identifier for this physical device. Two
        /// devices with identical DisplayName (e.g. two Logitech USB Microphones)
        /// must produce different StableIds.
        ///
        /// Backends should produce this string via <see cref="ComputeStableId"/>
        /// so callers (binding resolver, picker filter) can predict the same
        /// value from the raw (deviceId, name) pairs returned by
        /// <c>GlobalAudioHandler.GetAllInputDevices()</c> without instantiating
        /// the device first.
        /// </summary>
        public abstract string StableId { get; }

        /// <summary>
        /// Canonical formula for a device's StableId. Single source of truth
        /// for everything that needs to derive a StableId from a raw
        /// (deviceId, name) pair without holding a live <see cref="MicDevice"/>.
        /// </summary>
        public static string ComputeStableId(int deviceId, string name) => $"{name}@{deviceId}";

        protected MicDevice(string displayName)
        {
            DisplayName = displayName;
        }

        /// <summary>
        /// Resets the microphone streams and clears all buffers.
        /// </summary>
        /// <returns>0 if successful, otherwise an error code.</returns>
        public abstract int Reset();

        /// <summary>
        /// Dequeues an output frame from the microphone.
        /// </summary>
        /// <returns>
        /// Whether the dequeue was successful.
        /// </returns>
        public abstract bool DequeueOutputFrame(out MicOutputFrame frame);

        /// <summary>
        /// Clears the output queue.
        /// </summary>
        public abstract void ClearOutputQueue();

        /// <summary>
        /// Set the monitoring level of this Microphone Device.
        /// </summary>
        /// <param name="volume">The volume to set to.</param>
        public abstract void SetMonitoringLevel(float volume);

        /// <summary>
        /// Converts this microphone into its serialized form.
        /// </summary>
        public abstract SerializedMic Serialize();

        protected virtual void DisposeManagedResources() { }
        protected virtual void DisposeUnmanagedResources() { }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    DisposeManagedResources();
                }
                DisposeUnmanagedResources();
                _disposed = true;
            }
        }

        ~MicDevice()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
