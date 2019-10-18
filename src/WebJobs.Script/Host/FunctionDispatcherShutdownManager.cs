// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    internal sealed class FunctionDispatcherShutdownManager : IHostedService
    {
        private IFunctionDispatcher _functionDispatcher;

        public FunctionDispatcherShutdownManager(IFunctionDispatcher functionDispatcher)
        {
            _functionDispatcher = functionDispatcher;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.WhenAny(Task.Delay(5000), _functionDispatcher.ShutdownAsync());
        }
    }
}
