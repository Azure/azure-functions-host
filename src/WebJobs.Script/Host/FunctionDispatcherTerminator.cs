// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    internal sealed class FunctionDispatcherTerminator : IHostedService
    {
        private IEnumerable<IFunctionDispatcher> _functionDispatchers;

        public FunctionDispatcherTerminator(IEnumerable<IFunctionDispatcher> functionDispatchers)
        {
            _functionDispatchers = functionDispatchers;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            Task timeoutTask = Task.Delay(5000);
            foreach (IFunctionDispatcher currentDispatcher in _functionDispatchers)
            {
                await Task.WhenAny(timeoutTask, currentDispatcher.TerminateAsync());
            }
        }
    }
}
