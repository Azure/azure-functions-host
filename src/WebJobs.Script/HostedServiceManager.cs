// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class HostedServiceManager : IHostedService
    {
        private readonly IEnumerable<IHostedService> _hostedServices;

        public HostedServiceManager(IEnumerable<IHostedService> hostedServices)
        {
            _hostedServices = hostedServices;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (IHostedService hostedService in _hostedServices.Reverse())
            {
                if (hostedService is IManagedHostedService managedHostedService)
                {
                    await managedHostedService.StopServicesAsync(cancellationToken);
                }
            }
        }
    }
}