﻿namespace Unosquare.FFplayDotNet.Decoding
{
    using Core;
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;

    /// <summary>
    /// A base class for blocks of the deifferent MediaTypes.
    /// Blocks are the result of decoding and scaling a frame.
    /// Blocks have preallocated buffers wich makes them memory and CPU efficient
    /// Reue blocks as much as possible. Once you create a block from a frame,
    /// you don't need the frame anymore so make sure you dispose the frame.
    /// </summary>
    public abstract class MediaBlock : IComparable<MediaBlock>
    {
        /// <summary>
        /// Gets the media type of the data
        /// </summary>
        public abstract MediaType MediaType { get; }

        /// <summary>
        /// Gets the time at which this data should be presented (PTS)
        /// </summary>
        public TimeSpan StartTime { get; internal set; }

        /// <summary>
        /// Gets the amount of time this data has to be presented
        /// </summary>
        public TimeSpan Duration { get; internal set; }

        /// <summary>
        /// Gets the end time.
        /// </summary>
        public TimeSpan EndTime { get; internal set; }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="other">An object to compare with this instance.</param>
        /// <returns>
        /// A value that indicates the relative order of the objects being compared. The return value has these meanings: Value Meaning Less than zero This instance precedes <paramref name="other" /> in the sort order.  Zero This instance occurs in the same position in the sort order as <paramref name="other" />. Greater than zero This instance follows <paramref name="other" /> in the sort order.
        /// </returns>
        public int CompareTo(MediaBlock other)
        {
            return StartTime.CompareTo(other.StartTime);
        }
    }

}
