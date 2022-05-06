// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal sealed class FunctionInvocationDispatcherShutdownManager : IHostedService
    {
        private readonly IFunctionInvocationDispatcher _functionDispatcher;
        private readonly ILogger _logger;

        public FunctionInvocationDispatcherShutdownManager(IFunctionInvocationDispatcherFactory functionDispatcherFactory, ILogger logger)
        {
            _functionDispatcher = functionDispatcherFactory.GetFunctionDispatcher();
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _functionDispatcher.ShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping FunctionInvocationDispatcherShutdownManager Service. Handling error and continuing.");
            }
        }
    }
}
