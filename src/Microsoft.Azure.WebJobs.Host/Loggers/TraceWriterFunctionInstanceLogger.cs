// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
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
            string traceMessage = string.Format(CultureInfo.InvariantCulture, "Executing: '{0}' - Reason: '{1}'", message.Function.ShortName, message.FormatReason());
            Trace(TraceLevel.Info, message.HostInstanceId, message.Function, message.FunctionInstanceId, traceMessage, TraceSource.Execution);
            return Task.FromResult<string>(null);
        }

        public Task LogFunctionCompletedAsync(FunctionCompletedMessage message, CancellationToken cancellationToken)
        {
            if (message.Succeeded)
            {
                string traceMessage = string.Format(CultureInfo.InvariantCulture, "Executed: '{0}' (Succeeded)", message.Function.ShortName);
                Trace(TraceLevel.Info, message.HostInstanceId, message.Function, message.FunctionInstanceId, traceMessage, TraceSource.Execution);
            }
            else
            {
                string traceMessage = string.Format(CultureInfo.InvariantCulture, "Executed: '{0}' (Failed)", message.Function.ShortName);
                Trace(TraceLevel.Error, message.HostInstanceId, message.Function, message.FunctionInstanceId, traceMessage, TraceSource.Execution, message.Failure.Exception);

                // Also log the eror message using TraceSource.Host, to ensure
                // it gets written to Console
                traceMessage = string.Format(CultureInfo.InvariantCulture, "  Function had errors. See Azure WebJobs SDK dashboard for details. Instance ID is '{0}'", message.FunctionInstanceId);
                Trace(TraceLevel.Error, message.HostInstanceId, message.Function, message.FunctionInstanceId, traceMessage, TraceSource.Host, message.Failure.Exception);
            }
            return Task.FromResult(0);
        }

        private void Trace(TraceLevel level, Guid hostInstanceId, FunctionDescriptor descriptor, Guid functionId, string message, string source, Exception exception = null)
        {
            TraceEvent traceEvent = new TraceEvent(level, message, source, exception);
            traceEvent.AddFunctionInstanceDetails(hostInstanceId, descriptor, functionId);
            _trace.Trace(traceEvent);
        }

        public Task DeleteLogFunctionStartedAsync(string startedMessageId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
