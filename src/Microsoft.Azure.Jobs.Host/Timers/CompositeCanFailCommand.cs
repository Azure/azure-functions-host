using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    internal class CompositeCanFailCommand : ICanFailCommand
    {
        private readonly IEnumerable<ICanFailCommand> _commands;

        public CompositeCanFailCommand(params ICanFailCommand[] commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException("commands");
            }

            _commands = commands;
        }

        public bool TryExecute()
        {
            bool compositeSucceeded = true;

            foreach (ICanFailCommand command in _commands)
            {
                if (command == null)
                {
                    continue;
                }

                if (!command.TryExecute())
                {
                    // Mark the composite as failed if any inner command fails.
                    // We have to choose between executing some commands for frequently than necessary or executing
                    // other commands less frequently than necessary, so we choose the safe option.
                    compositeSucceeded = false;
                }
            }

            return compositeSucceeded;
        }
    }
}
