﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public abstract class LinuxContainerInitializationHostService : IHostedService
    {
        private readonly IEnvironment _environment;
        private readonly IInstanceManager _instanceManager;
        private readonly ILogger _logger;
        private readonly StartupContextProvider _startupContextProvider;
        private CancellationToken _cancellationToken;

        public LinuxContainerInitializationHostService(IEnvironment environment, IInstanceManager instanceManager, ILogger<LinuxContainerInitializationHostService> logger, StartupContextProvider startupContextProvider)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _logger = logger;
            _startupContextProvider = startupContextProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing LinuxContainerInitializationService.");
            _cancellationToken = cancellationToken;

            // The service should be registered in Linux Consumption only, but do additional check here.
            if (_environment.IsAnyLinuxConsumption())
            {
                await ApplyStartContextIfPresent();
            }
            else if (_environment.IsFlexConsumptionSku())
            {
                _logger.LogInformation("Container has (re)started. Waiting for specialization");
            }
        }

        private async Task ApplyStartContextIfPresent(CancellationToken cancellationToken)
        {
            var startContext = await GetStartContextOrNullAsync(cancellationToken);

            if (!string.IsNullOrEmpty(startContext))
            {
                _logger.LogInformation("Applying host context");

                var encryptedAssignmentContext = JsonConvert.DeserializeObject<EncryptedHostAssignmentContext>(startContext);
                var assignmentContext = _startupContextProvider.SetContext(encryptedAssignmentContext);
                await SpecializeMSISideCar(assignmentContext);

                bool success = _instanceManager.StartAssignment(assignmentContext);
                _logger.LogInformation($"StartAssignment invoked (Success={success})");
            }
            else
            {
                _logger.LogInformation("No host context specified. Waiting for host assignment");
            }
        }

        public abstract Task<string> GetStartContextOrNullAsync(CancellationToken cancellationToken);

        protected abstract Task SpecializeMSISideCar(HostAssignmentContext assignmentContext);

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
