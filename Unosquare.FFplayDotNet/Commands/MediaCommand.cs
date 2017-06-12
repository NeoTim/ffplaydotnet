﻿namespace Unosquare.FFplayDotNet.Commands
{
    using System.Threading.Tasks;

    /// <summary>
    /// Represents a command to be executed against an intance of the MediaElement
    /// </summary>
    internal abstract class MediaCommand
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaCommand" /> class.
        /// </summary>
        /// <param name="manager">The command manager.</param>
        /// <param name="commandType">Type of the command.</param>
        protected MediaCommand(MediaCommandManager manager, MediaCommandType commandType)
        {
            Manager = manager;
            CommandType = commandType;
            Promise = new Task(Execute);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the associated parent command manager
        /// </summary>
        public MediaCommandManager Manager { get; private set; }

        /// <summary>
        /// Gets the type of the command.
        /// </summary>
        public MediaCommandType CommandType { get; private set; }

        /// <summary>
        /// Gets the promise-mode Task. You can wait for this task
        /// </summary>
        public Task Promise { get; private set; }

        #endregion

        #region Methods

        /// <summary>
        /// Executes this command asynchronously
        /// by starting the associated promise and awaiting it.
        /// </summary>
        /// <returns></returns>
        public async Task ExecuteAsync()
        {
            var m = Manager.MediaElement;
            if (m.Commands.ExecutingCommand != null)
                await m.Commands.ExecutingCommand.Promise;

            m.Commands.ExecutingCommand = this;
            Promise.Start();
            await Promise;
            m.Commands.HasSeeked = true;
            m.Commands.ExecutingCommand = null;
        }

        /// <summary>
        /// Performs the actions that this command implements.
        /// </summary>
        protected abstract void Execute();

        #endregion
    }
}
