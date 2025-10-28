// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal sealed class FunctionInvocationDispatcherShutdownManager : IHostedService
    {
        private readonly IFunctionInvocationDispatcher _functionDispatcher;

        public FunctionInvocationDispatcherShutdownManager(IFunctionInvocationDispatcherFactory functionDispatcherFactory)
        {
            _functionDispatcher = functionDispatcherFactory.GetFunctionDispatcher();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _functionDispatcher.ShutdownAsync();
        }
    }
}
