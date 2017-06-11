﻿namespace Unosquare.FFplayDotNet.Rendering
{
    using Core;
    using Decoding;
    using Rendering.Wave;
    using System;
    using System.Threading;
    using System.Windows;

    /// <summary>
    /// Provides Audio Output capabilities by writing samples to the default audio output device.
    /// </summary>
    /// <seealso cref="Unosquare.FFplayDotNet.Rendering.Wave.IWaveProvider" />
    /// <seealso cref="Unosquare.FFplayDotNet.Rendering.IRenderer" />
    /// <seealso cref="System.IDisposable" />
    internal sealed class AudioRenderer : IDisposable, IRenderer, IWaveProvider
    {
        #region Private Members

        private readonly MediaElement MediaElement;
        private WavePlayer AudioDevice;
        private CircularBuffer AudioBuffer;
        private bool IsDisposed = false;

        private byte[] SilenceBuffer = null;
        private byte[] ReadBuffer = null;
        private double LeftVolume = 1.0d;
        private double RightVolume = 1.0d;

        private WaveFormat m_Format = null;
        private double m_Volume = 1.0d;
        private double m_Balance = 0.0d;
        private volatile bool m_IsMuted = false;

        private int BytesPerSample = 2;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioRenderer"/> class.
        /// </summary>
        /// <param name="mediaElement">The media element.</param>
        public AudioRenderer(MediaElement mediaElement)
        {
            MediaElement = mediaElement;

            m_Format = new WaveFormat(AudioParams.Output.SampleRate, AudioParams.OutputBitsPerSample, AudioParams.Output.ChannelCount);
            if (WaveFormat.BitsPerSample != 16 || WaveFormat.Channels != 2)
                throw new NotSupportedException("Wave Format has to be 16-bit and 2-channel.");

            BytesPerSample = WaveFormat.BitsPerSample / 8;
            SilenceBuffer = new byte[m_Format.BitsPerSample / 8 * m_Format.Channels * 2];

            if (MediaElement.HasAudio)
                Initialize();

            if (Application.Current != null)
                mediaElement.InvokeOnUI(() => {
                    Application.Current.Exit += (s, e) =>
                    {
                        Destroy();
                    };
                });
        }

        #endregion

        #region Initialization and Destruction

        /// <summary>
        /// Initializes the audio renderer.
        /// Call the Play Method to start reading samples
        /// </summary>
        private void Initialize()
        {
            Destroy();

            AudioDevice = new WavePlayer()
            {
                DesiredLatency = 200,
                NumberOfBuffers = 2,
            };

            var bufferLength = WaveFormat.ConvertLatencyToByteSize(AudioDevice.DesiredLatency) * MediaElement.Blocks[MediaType.Audio].Capacity / 2;
            AudioBuffer = new CircularBuffer(bufferLength);
            AudioDevice.Init(this);
        }


        /// <summary>
        /// Destroys the audio renderer.
        /// Makes it useless.
        /// </summary>
        private void Destroy()
        {
            if (AudioDevice != null)
            {
                AudioDevice.Stop();
                AudioDevice.Dispose();
                AudioDevice = null;
            }

            if (AudioBuffer != null)
            {
                AudioBuffer.Dispose();
                AudioBuffer = null;
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the output format of the audio
        /// </summary>
        public WaveFormat WaveFormat
        {
            get { return m_Format; }
        }


        /// <summary>
        /// Gets or sets the volume.
        /// </summary>
        /// <value>
        /// The volume.
        /// </value>
        public double Volume
        {
            get { return Thread.VolatileRead(ref m_Volume); }
            set
            {
                if (value < 0) value = 0;
                if (value > 1) value = 1;

                var leftFactor = m_Balance > 0 ? 1d - m_Balance : 1d;
                var rightFactor = m_Balance < 0 ? 1d + m_Balance : 1d;

                LeftVolume = leftFactor * value;
                RightVolume = rightFactor * value;
                Thread.VolatileWrite(ref m_Volume, value);
            }
        }

        /// <summary>
        /// Gets or sets the balance (-1.0 to 1.0).
        /// </summary>
        public double Balance
        {
            get { return Thread.VolatileRead(ref m_Balance); }
            set
            {
                if (value < -1.0) value = -1.0;
                if (value > 1.0) value = 1.0;
                Thread.VolatileWrite(ref m_Balance, value);
                Volume = Thread.VolatileRead(ref m_Volume);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the wave output is muted.
        /// </summary>
        public bool IsMuted
        {
            get { return m_IsMuted; }
            set { m_IsMuted = value; }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Renders the specified media block.
        /// </summary>
        /// <param name="mediaBlock">The media block.</param>
        /// <param name="clockPosition">The clock position.</param>
        /// <param name="renderIndex">Index of the render.</param>
        public void Render(MediaBlock mediaBlock, TimeSpan clockPosition, int renderIndex)
        {
            if (AudioBuffer == null) return;
            var block = mediaBlock as AudioBlock;
            if (block == null) return;

            var currentIndex = renderIndex;
            var audioBlocks = MediaElement.Blocks[MediaType.Audio];
            var addedBlockCount = 0;
            var addedBytes = 0;
            while (currentIndex >= 0 && currentIndex < audioBlocks.Count)
            {
                var audioBlock = audioBlocks[currentIndex] as AudioBlock;
                if (AudioBuffer.WriteTag < audioBlock.StartTime)
                {
                    AudioBuffer.Write(audioBlock.Buffer, audioBlock.BufferLength, audioBlock.StartTime);
                    addedBlockCount++;
                    addedBytes += audioBlock.BufferLength;
                }

                currentIndex++;

                // Stop adding if we have too much in there.
                if (AudioBuffer.CapacityPercent >= 0.8)
                    break;
            }
        }

        /// <summary>
        /// Executed when the Play method is called on the parent MediaElement
        /// </summary>
        public void Play()
        {
            AudioDevice?.Play();
        }

        /// <summary>
        /// Executed when the Pause method is called on the parent MediaElement
        /// </summary>
        public void Pause()
        {
            //AudioDevice?.Pause();
        }

        /// <summary>
        /// Executed when the Pause method is called on the parent MediaElement
        /// </summary>
        public void Stop()
        {
            //AudioDevice?.Stop();
            AudioBuffer.Clear();
        }

        /// <summary>
        /// Executed when the Close method is called on the parent MediaElement
        /// </summary>
        public void Close()
        {
            Destroy();
        }

        #endregion

        #region IWaveProvider Support

        /// <summary>
        /// Called whenever the audio driver requests samples.
        /// Do not call this method directly.
        /// </summary>
        /// <param name="renderBuffer">The render buffer.</param>
        /// <param name="renderBufferOffset">The render buffer offset.</param>
        /// <param name="requestedBytes">The requested bytes.</param>
        /// <returns></returns>
        public int Read(byte[] renderBuffer, int renderBufferOffset, int requestedBytes)
        {
            if (MediaElement.IsPlaying == false || MediaElement.HasAudio == false || AudioBuffer.ReadableCount <= 0)
            {
                Buffer.BlockCopy(SilenceBuffer, 0, renderBuffer, renderBufferOffset, Math.Min(SilenceBuffer.Length, renderBuffer.Length));
                return SilenceBuffer.Length;
            }

            if (ReadBuffer == null || ReadBuffer.Length != requestedBytes)
                ReadBuffer = new byte[requestedBytes];

            requestedBytes = Math.Min(requestedBytes, AudioBuffer.ReadableCount);
            AudioBuffer.Read(requestedBytes, ReadBuffer, 0);

            // Samples are interleaved (left and right in 16-bit each)
            var isLeftSample = true;
            for (var baseIndex = 0; baseIndex < ReadBuffer.Length; baseIndex += BytesPerSample)
            {
                // The sample has 2 bytes: at the base index is the LSB and at the baseIndex + 1 is the MSB
                // this obviously only holds true for Little Endian architectures, and thus, the current code is not portable.
                // This replaces BitConverter.ToInt16(ReadBuffer, baseIndex); which is obviously much slower.
                var sample = (short)(ReadBuffer[baseIndex] + (short)(ReadBuffer[baseIndex + 1] << 8));  
                
                if (IsMuted)
                {
                    sample = 0;
                }
                else
                {
                    if (isLeftSample && LeftVolume != 1.0)
                        sample = (short)(sample * LeftVolume);
                    else if (isLeftSample == false && RightVolume != 1.0)
                        sample = (short)(sample * RightVolume);
                }

                renderBuffer[baseIndex] = (byte)(sample & 0xff);
                renderBuffer[baseIndex + 1] = (byte)(sample >> 8);
                isLeftSample = !isLeftSample;
            }

            return requestedBytes;
        }

        #endregion

        #region IDisposable Support

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing">
        ///   <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                    Destroy();

                IsDisposed = true;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion

    }
}
