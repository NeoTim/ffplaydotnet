﻿namespace Unosquare.FFplayDotNet.Core
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// A fixed-size buffer that acts as an infinite length one.
    /// This buffer is backed by unmanaged, very fast memory so ensure you call
    /// the dispose method when you are donde using it.
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    internal sealed class CircularBuffer : IDisposable
    {

        #region Private Statae Variables

        /// <summary>
        /// The unbmanaged buffer
        /// </summary>
        private IntPtr Buffer = IntPtr.Zero;

        /// <summary>
        /// The locking object to perform synchronization.
        /// </summary>
        private readonly object SyncLock = new object();

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="CircularBuffer"/> class.
        /// </summary>
        /// <param name="bufferLength">Length of the buffer.</param>
        public CircularBuffer(int bufferLength)
        {
            Length = bufferLength;
            Buffer = Marshal.AllocHGlobal(Length);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the capacity of this buffer.
        /// </summary>
        public int Length { get; private set; }

        /// <summary>
        /// Gets the current, 0-based read index
        /// </summary>
        public int ReadIndex { get; private set; }

        /// <summary>
        /// Gets the current, 0-based write index.
        /// </summary>
        public int WriteIndex { get; private set; }

        /// <summary>
        /// Gets an the object associated with the last write
        /// </summary>
        public TimeSpan WriteTag { get; private set; } = TimeSpan.MinValue;

        /// <summary>
        /// Gets the available bytes to read.
        /// </summary>
        public int ReadableCount { get; private set; }

        /// <summary>
        /// Gets the number of bytes that can be written.
        /// </summary>
        public int WritableCount { get { return Length - ReadableCount; } }

        /// <summary>
        /// Gets percentage of used bytes (readbale/available, from 0.0 to 1.0).
        /// </summary>
        public double CapacityPercent { get { return 1.0 * ReadableCount / Length; } }

        #endregion

        #region Methods

        /// <summary>
        /// Reads the specified requested bytes.
        /// </summary>
        /// <param name="requestedBytes">The requested bytes.</param>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException"></exception>
        public byte[] Read(int requestedBytes)
        {
            lock (SyncLock)
            {
                if (requestedBytes > ReadableCount)
                    throw new InvalidOperationException(
                        $"Unable to read {requestedBytes} bytes. Only {ReadableCount} bytes are available");

                var result = new byte[requestedBytes];

                var readCount = 0;
                while (readCount < requestedBytes)
                {
                    var copyLength = Math.Min(Length - ReadIndex, requestedBytes - readCount);
                    var sourcePtr = Buffer + ReadIndex;
                    Marshal.Copy(sourcePtr, result, readCount, copyLength);

                    readCount += copyLength;
                    ReadIndex += copyLength;
                    ReadableCount -= copyLength;

                    if (ReadIndex >= Length)
                        ReadIndex = 0;
                }

                return result;
            }
        }

        /// <summary>
        /// Reads the specified number of bytes into the target array.
        /// </summary>
        /// <param name="requestedBytes">The requested bytes.</param>
        /// <param name="target">The target.</param>
        /// <exception cref="System.InvalidOperationException"></exception>
        public void Read(int requestedBytes, byte[] target)
        {
            lock (SyncLock)
            {
                if (requestedBytes > ReadableCount)
                    throw new InvalidOperationException(
                        $"Unable to read {requestedBytes} bytes. Only {ReadableCount} bytes are available");

                var readCount = 0;
                while (readCount < requestedBytes)
                {
                    var copyLength = Math.Min(Length - ReadIndex, requestedBytes - readCount);
                    var sourcePtr = Buffer + ReadIndex;
                    Marshal.Copy(sourcePtr, target, readCount, copyLength);

                    readCount += copyLength;
                    ReadIndex += copyLength;
                    ReadableCount -= copyLength;

                    if (ReadIndex >= Length)
                        ReadIndex = 0;
                }
            }
        }

        /// <summary>
        /// Writes data to the backing buffer using the specified pointer and length.
        /// and associating a write tag for this operation.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="length">The length.</param>
        /// <param name="writeTag">The write tag.</param>
        /// <exception cref="System.InvalidOperationException">Read</exception>
        public void Write(IntPtr source, int length, TimeSpan writeTag)
        {
            lock (SyncLock)
            {
                if (ReadableCount + length > Length)
                    throw new InvalidOperationException(
                        $"Unable to write to circular buffer. Call the {nameof(Read)} method to make some additional room");

                var writeCount = 0;
                while (writeCount < length)
                {
                    var copyLength = Math.Min(Length - WriteIndex, length - writeCount);
                    var sourcePtr = source + writeCount;
                    var targetPtr = Buffer + WriteIndex;
                    Utils.CopyMemory(targetPtr, sourcePtr, (uint)copyLength);

                    writeCount += copyLength;
                    WriteIndex += copyLength;
                    ReadableCount += copyLength;

                    if (WriteIndex >= Length)
                        WriteIndex = 0;
                }

                WriteTag = writeTag;
            }
        }

        /// <summary>
        /// Resets all states as if this buffer had just been created.
        /// </summary>
        public void Clear()
        {
            lock (SyncLock)
            {
                WriteIndex = 0;
                ReadIndex = 0;
                WriteTag = TimeSpan.MinValue;
                ReadableCount = 0;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (Buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Buffer);
                Buffer = IntPtr.Zero;
                Length = 0;
            }
        }

        #endregion
    }
}
