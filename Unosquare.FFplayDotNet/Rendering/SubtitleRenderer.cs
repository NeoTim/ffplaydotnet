﻿namespace Unosquare.FFplayDotNet.Rendering
{
    using System;
    using Decoding;

    /// <summary>
    /// Subtitle Renderer - Does nothing at this point.
    /// </summary>
    /// <seealso cref="Unosquare.FFplayDotNet.Rendering.IRenderer" />
    internal class SubtitleRenderer : IRenderer
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="SubtitleRenderer"/> class.
        /// </summary>
        /// <param name="mediaElement">The media element.</param>
        public SubtitleRenderer(MediaElement mediaElement)
        {
            //placeholder
        }

        /// <summary>
        /// Executed when the Close method is called on the parent MediaElement
        /// </summary>
        public void Close()
        {
            //placeholder
        }

        /// <summary>
        /// Executed when the Pause method is called on the parent MediaElement
        /// </summary>
        public void Pause()
        {
            //placeholder
        }

        /// <summary>
        /// Executed when the Play method is called on the parent MediaElement
        /// </summary>
        public void Play()
        {
            //placeholder
        }

        /// <summary>
        /// Renders the specified media block.
        /// </summary>
        /// <param name="mediaBlock">The media block.</param>
        /// <param name="clockPosition">The clock position.</param>
        /// <param name="renderIndex">Index of the render.</param>
        public void Render(MediaBlock mediaBlock, TimeSpan clockPosition, int renderIndex)
        {
            //placeholder
        }

        /// <summary>
        /// Executed when the Pause method is called on the parent MediaElement
        /// </summary>
        public void Stop()
        {
            //placeholder
        }
    }
}
