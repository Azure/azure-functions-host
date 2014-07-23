using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Timers
{
    internal static class IntervalSeparationCommandExtensions
    {
        public static void Execute(this IIntervalSeparationCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            command.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
