﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Unosquare.FFplayDotNet.Commands
{
    /// <summary>
    /// Implements the logic to pause and rewind the media stream
    /// </summary>
    /// <seealso cref="Unosquare.FFplayDotNet.Commands.MediaCommand" />
    internal sealed class StopCommand : MediaCommand
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StopCommand" /> class.
        /// </summary>
        /// <param name="manager">The media element.</param>
        public StopCommand(MediaCommandManager manager) 
            : base(manager, MediaCommandType.Stop)
        {

        }

        /// <summary>
        /// Performs the actions that this command implements.
        /// </summary>
        protected override void Execute()
        {
            var m = Manager.MediaElement;
            foreach (var renderer in m.Renderers.Values)
                renderer.Stop();

            m.Clock.Reset();
            m.Commands.Seek(TimeSpan.Zero);
            
        }
    }
}
