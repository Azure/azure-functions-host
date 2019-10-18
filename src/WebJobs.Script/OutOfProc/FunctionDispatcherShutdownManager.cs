﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal sealed class FunctionDispatcherShutdownManager : IHostedService
    {
        private readonly IFunctionDispatcher _functionDispatcher;
        private readonly ILogger _logger;

        public FunctionDispatcherShutdownManager(IFunctionDispatcher functionDispatcher, ILogger<FunctionDispatcherShutdownManager> logger)
        {
            _functionDispatcher = functionDispatcher;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task timeoutTask = Task.Delay(5000);
            Task completedTask = await Task.WhenAny(timeoutTask, _functionDispatcher.ShutdownAsync());
            if (completedTask.Equals(timeoutTask))
            {
                _logger.LogDebug("FunctionDispatcher StopAsync() completing due to timeout");
            }
            else
            {
                _logger.LogDebug("FunctionDispatcher StopAsync() completing due to completion of in-buffer tasks");
            }
        }
    }
}
