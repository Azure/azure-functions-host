// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class ConsoleFunctionInstanceLogger : IFunctionInstanceLogger
    {
        public Task<string> LogFunctionStartedAsync(FunctionStartedMessage message, CancellationToken cancellationToken)
        {
            Console.WriteLine("Executing: '{0}' because {1}", message.Function.ShortName, message.FormatReason());
            return Task.FromResult<string>(null);
        }

        public Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
        {
            if (!message.Succeeded)
            {
                var oldColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(
                    "  Function had errors. See Azure WebJobs SDK dashboard for details. Instance id is {0}",
                    message.FunctionInstanceId);
                Console.ForegroundColor = oldColor;
            }

            return Task.FromResult(0);
        }

        public Task DeleteLogFunctionStartedAsync(string startedMessageId, CancellationToken cancellationToken)
        {
            // Intentionally left blank to avoid too much verbosity
            return Task.FromResult(0);
        }
    }
}
