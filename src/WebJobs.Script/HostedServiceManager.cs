// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class HostedServiceManager : IHostedService
    {
        private readonly IEnumerable<IManagedHostedService> _managedHostedServices;

        public HostedServiceManager(IEnumerable<IManagedHostedService> managedHostedServices)
        {
            _managedHostedServices = managedHostedServices;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (IManagedHostedService managedHostedService in _managedHostedServices)
            {
                await managedHostedService.OuterStopAsync(cancellationToken);
            }
        }
    }
}