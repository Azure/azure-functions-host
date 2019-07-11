// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.ServiceManagers
{
    internal class HostedServiceManager : IHostedService
    {
        private readonly IEnumerable<IHostedService> hostedServices;

        public HostedServiceManager(IEnumerable<IHostedService> hostedServices)
        {
            this.hostedServices = hostedServices;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (IHostedService hostedService in hostedServices)
            {
                if (hostedService is IManagedHostedService managedHostedService)
                {
                    await managedHostedService.StopServicesAsync(cancellationToken);
                }
            }
        }
    }
}