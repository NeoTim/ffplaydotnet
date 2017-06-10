﻿namespace Unosquare.FFplayDotNet.Rendering.Wave
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A wave player that opens an audio device and continuously feeds it
    /// with audio samples using a wave provider.
    /// </summary>
    internal class WavePlayer
    {
        #region State Variables

        private readonly object WaveOutLock = new object();
        private readonly SynchronizationContext SyncContext;
        private IntPtr DeviceHandle;
        private WaveOutBuffer[] Buffers;
        private IWaveProvider WaveStream;
        private AutoResetEvent CallbackEvent;

        private volatile PlaybackState m_PlaybackState;
        private int m_DeviceNumber = -1;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="WavePlayer"/> class.
        /// </summary>
        public WavePlayer()
        {
            SyncContext = SynchronizationContext.Current;
            if (SyncContext != null &&
                ((SyncContext.GetType().Name == "LegacyAspNetSynchronizationContext") ||
                (SyncContext.GetType().Name == "AspNetSynchronizationContext")))
            {
                SyncContext = null;
            }

            // set default values up
            DeviceNumber = 0;
            DesiredLatency = 300;
            NumberOfBuffers = 2;
        }


        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the desired latency in milliseconds
        /// Should be set before a call to Init
        /// </summary>
        public int DesiredLatency { get; set; }

        /// <summary>
        /// Gets or sets the number of buffers used
        /// Should be set before a call to Init
        /// </summary>
        public int NumberOfBuffers { get; set; }

        /// <summary>
        /// Gets or sets the device number
        /// Should be set before a call to Init
        /// This must be between -1 and <see>DeviceCount</see> - 1.
        /// -1 means stick to default device even default device is changed
        /// </summary>
        public int DeviceNumber
        {
            get { return m_DeviceNumber; }
            set
            {
                m_DeviceNumber = value;
                lock (WaveOutLock)
                {
                    WaveOutCapabilities caps;
                    WaveInterop.waveOutGetDevCaps((IntPtr)m_DeviceNumber, out caps, Marshal.SizeOf(typeof(WaveOutCapabilities)));
                    Capabilities = caps;
                }
            }
        }

        /// <summary>
        /// Gets a <see cref="Wave.WaveFormat"/> instance indicating the format the hardware is using.
        /// </summary>
        public WaveFormat OutputWaveFormat
        {
            get { return WaveStream.WaveFormat; }
        }

        /// <summary>
        /// Playback State
        /// </summary>
        public PlaybackState PlaybackState
        {
            get { return m_PlaybackState; }
        }

        /// <summary>
        /// Gets the capabilities.
        /// </summary>
        public WaveOutCapabilities Capabilities { get; private set; }

        #endregion

        #region Public API

        /// <summary>
        /// Initializes the specified wave provider.
        /// </summary>
        /// <param name="waveProvider">The wave provider.</param>
        /// <exception cref="System.InvalidOperationException">Can't re-initialize during playback</exception>
        public void Init(IWaveProvider waveProvider)
        {
            if (m_PlaybackState != PlaybackState.Stopped)
                throw new InvalidOperationException("Can't re-initialize during playback");

            if (DeviceHandle != IntPtr.Zero)
            {
                // normally we don't allow calling Init twice, but as experiment, see if we can clean up and go again
                // try to allow reuse of this waveOut device
                // n.b. risky if Playback thread has not exited
                DisposeBuffers();
                CloseWaveOut();
            }

            CallbackEvent = new AutoResetEvent(false);

            WaveStream = waveProvider;
            var bufferSize = waveProvider.WaveFormat.ConvertLatencyToByteSize((DesiredLatency + NumberOfBuffers - 1) / NumberOfBuffers);

            MmResult result;
            lock (WaveOutLock)
            {
                result = WaveInterop.waveOutOpenWindow(out DeviceHandle, (IntPtr)DeviceNumber, WaveStream.WaveFormat,
                    CallbackEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, WaveInterop.WaveInOutOpenFlags.CallbackEvent);
            }

            MmException.Try(result, nameof(WaveInterop.waveOutOpen));

            Buffers = new WaveOutBuffer[NumberOfBuffers];
            m_PlaybackState = PlaybackState.Stopped;
            for (var n = 0; n < NumberOfBuffers; n++)
            {
                Buffers[n] = new WaveOutBuffer(DeviceHandle, bufferSize, WaveStream, WaveOutLock);
            }
        }

        /// <summary>
        /// Start playing the audio from the WaveStream
        /// </summary>
        public void Play()
        {
            if (Buffers == null || WaveStream == null)
            {
                throw new InvalidOperationException($"Must call {nameof(Init)} first");
            }
            if (m_PlaybackState == PlaybackState.Stopped)
            {
                m_PlaybackState = PlaybackState.Playing;
                CallbackEvent.Set(); // give the thread a kick
                ThreadPool.QueueUserWorkItem(state => StartPlaybackThread(), null);
            }
            else if (m_PlaybackState == PlaybackState.Paused)
            {
                Resume();
                CallbackEvent.Set(); // give the thread a kick
            }
        }

        /// <summary>
        /// Pause the audio
        /// </summary>
        public void Pause()
        {
            if (m_PlaybackState != PlaybackState.Playing) return;

            MmResult result;
            m_PlaybackState = PlaybackState.Paused; // set this here to avoid a deadlock problem with some drivers
            lock (WaveOutLock)
                result = WaveInterop.waveOutPause(DeviceHandle);

            if (result != MmResult.NoError)
                throw new MmException(result, nameof(WaveInterop.waveOutPause));
        }

        /// <summary>
        /// Resume playing after a pause from the same position
        /// </summary>
        private void Resume()
        {
            if (m_PlaybackState != PlaybackState.Paused) return;

            MmResult result;
            lock (WaveOutLock)
                result = WaveInterop.waveOutRestart(DeviceHandle);

            if (result != MmResult.NoError)
                throw new MmException(result, nameof(WaveInterop.waveOutRestart));

            m_PlaybackState = PlaybackState.Playing;

        }

        /// <summary>
        /// Stop and reset the WaveOut device
        /// </summary>
        public void Stop()
        {
            if (m_PlaybackState != PlaybackState.Stopped) return;

            // in the call to waveOutReset with function callbacks
            // some drivers will block here until OnDone is called
            // for every buffer
            m_PlaybackState = PlaybackState.Stopped; // set this here to avoid a problem with some drivers whereby 
            MmResult result;
            lock (WaveOutLock)
                result = WaveInterop.waveOutReset(DeviceHandle);

            if (result != MmResult.NoError)
                throw new MmException(result, nameof(WaveInterop.waveOutReset));

            CallbackEvent.Set(); // give the thread a kick, make sure we exit

        }

        /// <summary>
        /// Gets the current position in bytes from the wave output device.
        /// (n.b. this is not the same thing as the position within your reader
        /// stream - it calls directly into waveOutGetPosition)
        /// </summary>
        /// <returns>Position in bytes</returns>
        public long GetPosition()
        {
            lock (WaveOutLock)
            {
                var mmTime = new MmTime();
                mmTime.wType = MmTime.TIME_BYTES;
                MmException.Try(WaveInterop.waveOutGetPosition(DeviceHandle, out mmTime, Marshal.SizeOf(mmTime)), nameof(WaveInterop.waveOutGetPosition));

                if (mmTime.wType != MmTime.TIME_BYTES)
                    throw new Exception(string.Format($"{nameof(WaveInterop.waveOutGetPosition)}: wType -> Expected {0}, Received {1}", MmTime.TIME_BYTES, mmTime.wType));

                return mmTime.cb;
            }
        }

        #endregion

        #region Threading

        /// <summary>
        /// Starts the playback thread.
        /// </summary>
        private void StartPlaybackThread()
        {
            try
            {
                PerformContinuousPlayback();
            }
            catch (Exception e)
            {
                Debug.WriteLine($"WRN: Audio Playback thread exiting. {e.Message}");
            }
            finally
            {
                m_PlaybackState = PlaybackState.Stopped;
            }
        }

        /// <summary>
        /// Performs the continuous playback.
        /// </summary>
        private void PerformContinuousPlayback()
        {
            var queued = 0;
            while (m_PlaybackState != PlaybackState.Stopped)
            {
                if (!CallbackEvent.WaitOne(DesiredLatency) && m_PlaybackState == PlaybackState.Playing)
                    Debug.WriteLine("WARNING: WaveOutEvent callback event timeout");

                if (m_PlaybackState != PlaybackState.Playing)
                    continue;

                queued = 0; // requeue any buffers returned to us
                if (Buffers != null)
                    foreach (var buffer in Buffers)
                        if (buffer.InQueue || buffer.OnDone())
                            queued++;

                if (queued == 0)
                {
                    // we got to the end
                    m_PlaybackState = PlaybackState.Stopped;
                    CallbackEvent?.Set();
                }
            }
        }

        #endregion

        #region IDispose Pattern

        /// <summary>
        /// Closes this WaveOut device
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        /// <summary>
        /// Closes the WaveOut device and disposes of buffers
        /// </summary>
        /// <param name="disposing">True if called from <see>Dispose</see></param>
        protected void Dispose(bool disposing)
        {
            Stop();

            if (disposing)
            {
                DisposeBuffers();
            }

            CloseWaveOut();
        }

        /// <summary>
        /// Closes the wave device.
        /// </summary>
        private void CloseWaveOut()
        {
            if (CallbackEvent != null)
            {
                CallbackEvent.Close();
                CallbackEvent = null;
            }
            lock (WaveOutLock)
            {
                if (DeviceHandle != IntPtr.Zero)
                {
                    WaveInterop.waveOutClose(DeviceHandle);
                    DeviceHandle = IntPtr.Zero;
                }
            }
        }

        /// <summary>
        /// Disposes the buffers.
        /// </summary>
        private void DisposeBuffers()
        {
            if (Buffers != null)
            {
                foreach (var buffer in Buffers)
                {
                    buffer.Dispose();
                }
                Buffers = null;
            }
        }

        /// <summary>
        /// Finalizer. Only called when user forgets to call <see>Dispose</see>
        /// </summary>
        ~WavePlayer()
        {
            Dispose(false);
            Debug.Assert(false, "WaveOutEvent device was not closed");
        }

        #endregion

    }
}