// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    /// <summary>
    /// This logger only exists to log error messages in color for failed invocations.
    /// Executing/Executed logs are already written to Console by <see cref="TraceWriterFunctionInstanceLogger"/>.
    /// </summary>
    internal class ConsoleFunctionInstanceLogger : IFunctionInstanceLogger
    {
        public Task<string> LogFunctionStartedAsync(FunctionStartedMessage message, CancellationToken cancellationToken)
        {
            return Task.FromResult<string>(null);
        }

        public Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
        {
            if (!message.Succeeded)
            {
                var oldColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  Function had errors. See Azure WebJobs SDK dashboard for details. Instance ID is '{0}'", message.FunctionInstanceId);
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
