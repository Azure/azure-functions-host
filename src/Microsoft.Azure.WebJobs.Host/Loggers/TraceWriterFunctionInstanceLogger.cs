// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class TraceWriterFunctionInstanceLogger : IFunctionInstanceLogger
    {
        private TraceWriter _trace;

        public TraceWriterFunctionInstanceLogger(TraceWriter trace)
        {
            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }
            _trace = trace;
        }

        public Task<string> LogFunctionStartedAsync(FunctionStartedMessage message, CancellationToken cancellationToken)
        {
            _trace.Info(string.Format(CultureInfo.InvariantCulture, "Executing: '{0}' - Reason: '{1}'", message.Function.ShortName, message.FormatReason()), TraceSource.Execution);
            return Task.FromResult<string>(null);
        }

        public Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
        {
            if (message.Succeeded)
            {
                _trace.Info(string.Format(CultureInfo.InvariantCulture, "Executed: '{0}' (Succeeded)", message.Function.ShortName), source: TraceSource.Execution);
            }
            else
            {
                _trace.Error(string.Format(CultureInfo.InvariantCulture, "Executed: '{0}' (Failed)", message.Function.ShortName), message.Failure.Exception, TraceSource.Execution);

                // Also log the eror message using TraceSource.Host, to ensure
                // it gets written to Console
                _trace.Error(string.Format(CultureInfo.InvariantCulture, 
                    "  Function had errors. See Azure WebJobs SDK dashboard for details. Instance ID is '{0}'", message.FunctionInstanceId), message.Failure.Exception, TraceSource.Host);
            }
            return Task.FromResult(0);
        }

        public Task DeleteLogFunctionStartedAsync(string startedMessageId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
