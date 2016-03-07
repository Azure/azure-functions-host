// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Loggers;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    internal static class UpdateOutputLogCommandExtensions
    {
        public static bool TryExecute(this UpdateOutputLogCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            return command.TryExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public static void SaveAndClose(this UpdateOutputLogCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }

            command.SaveAndCloseAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
