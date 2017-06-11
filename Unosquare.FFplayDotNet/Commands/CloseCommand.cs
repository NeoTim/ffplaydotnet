﻿namespace Unosquare.FFplayDotNet.Commands
{
    using System;

    /// <summary>
    /// Implements the logic to close a media stream.
    /// </summary>
    /// <seealso cref="Unosquare.FFplayDotNet.Commands.MediaCommand" />
    internal sealed class CloseCommand : MediaCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CloseCommand"/> class.
        /// </summary>
        /// <param name="mediaElement">The media element.</param>
        public CloseCommand(MediaElement mediaElement)
            : base(mediaElement, MediaCommandType.Close)
        {
            // placeholder
        }

        /// <summary>
        /// Executes this command.
        /// </summary>
        protected override void Execute()
        {
            var m = MediaElement;

            if (m.IsOpen == false || m.IsOpening) return;

            m.Container?.Log(MediaLogMessageType.Debug, $"{nameof(CloseCommand)}: Entered");
            m.Clock.Pause();
            m.UpdatePosition(TimeSpan.Zero);

            m.IsTaskCancellationPending = true;

            // Wait for cycles to complete.
            m.BlockRenderingCycle.Wait();
            m.FrameDecodingCycle.Wait();
            m.PacketReadingCycle.Wait();

            // Wait for threads to finish
            m.BlockRenderingTask?.Join();
            m.FrameDecodingTask?.Join();
            m.PacketReadingTask?.Join();

            // Set the threads to null
            m.BlockRenderingTask = null;
            m.FrameDecodingTask = null;
            m.PacketReadingTask = null;

            // Call close on all renderers and clear them
            foreach (var renderer in m.Renderers.Values) renderer.Close();
            m.Renderers.Clear();

            // Reset the clock
            m.Clock.Reset();

            // Dispose the container
            if (m.Container != null)
            {
                m.Container?.Log(MediaLogMessageType.Debug, $"{nameof(CloseCommand)}: Completed");
                m.Container.Dispose();
                m.Container = null;
            }

            // Dispose the Blocks for all components
            foreach (var kvp in m.Blocks) kvp.Value.Dispose();
            m.Blocks.Clear();

            // Dispose the Frames for all components
            foreach (var kvp in m.Frames) kvp.Value.Dispose();
            m.Frames.Clear();

            // Clear the render times
            m.LastRenderTime.Clear();

            // Update notification properties
            m.InvokeOnUI(() =>
            {
                MediaElement.NotifyPropertyChanges();
            });

            m.MediaState = System.Windows.Controls.MediaState.Close;
        }
    }
}
