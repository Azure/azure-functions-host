// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry
{
    public class OpenTelemetryEventListenerService : IHostedService, IDisposable
    {
        private readonly OpenTelemetryEventListener _listener;

        public OpenTelemetryEventListenerService(EventLevel eventLevel)
        {
            _listener = new OpenTelemetryEventListener(eventLevel);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Listener initialization can be handled here if needed
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Dispose of your listener or handle any cleanup
            _listener.Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _listener?.Dispose();
        }
    }
}