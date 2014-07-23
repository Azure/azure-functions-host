using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Loggers;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Loggers
{
    internal static class UpdateOutputLogCommandExtensions
    {
        public static bool TryExecute(this UpdateOutputLogCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            return command.TryExecuteAsync(CancellationToken.None).Result;
        }

        public static void SaveAndClose(this UpdateOutputLogCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            command.SaveAndCloseAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
