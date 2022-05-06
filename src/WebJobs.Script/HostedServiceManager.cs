// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class HostedServiceManager : IHostedService
    {
        private readonly IEnumerable<IManagedHostedService> _managedHostedServices;
        private readonly ILogger _logger;

        public HostedServiceManager(IEnumerable<IManagedHostedService> managedHostedServices, ILogger logger)
        {
            _managedHostedServices = managedHostedServices;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (IManagedHostedService managedHostedService in _managedHostedServices)
            {
                try
                {
                    await managedHostedService.OuterStopAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping HostedServiceManager Service. Handling error and continuing.");
                }
            }
        }
    }
}