// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class FunctionListener : IListener
    {
        private readonly IListener _listener;
        private readonly FunctionDescriptor _descriptor;
        private readonly TraceWriter _trace;
        private readonly ILogger _logger;
        private bool _started = false;

        /// <summary>
        /// Wraps a listener. If the listener throws an exception OnStart,
        /// it attempts to recover by passing the exception through the trace pipeline.
        /// </summary>
        /// <param name="listener"></param>
        /// <param name="descriptor"></param>
        /// <param name="trace"></param>
        /// <param name="loggerFactory"></param>
        public FunctionListener(IListener listener, FunctionDescriptor descriptor, TraceWriter trace, ILoggerFactory loggerFactory)
        {
            _listener = listener;
            _descriptor = descriptor;
            _trace = trace;
            _logger = loggerFactory?.CreateLogger(LogCategories.Startup);
        }

        public void Cancel()
        {
            _listener.Cancel();
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _listener.StartAsync(cancellationToken);
                _started = true;
            }
            catch (Exception e)
            {
                new FunctionListenerException(_descriptor.ShortName, e).TryRecover(_trace, _logger);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_started)
            {
                await _listener.StopAsync(cancellationToken);
                _started = false;
            }
        }
    }
}
