// Copyright (c) .NET Foundation. All rights reserved.
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
    public abstract class LinuxContainerInitializationHostedService : IHostedService
    {
        private readonly IEnvironment _environment;
        private readonly IInstanceManager _instanceManager;
        private readonly ILogger _logger;
        private readonly StartupContextProvider _startupContextProvider;
        private CancellationToken _cancellationToken;

        public LinuxContainerInitializationHostedService(IEnvironment environment, IInstanceManager instanceManager, ILogger logger, StartupContextProvider startupContextProvider)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _logger = logger;
            _startupContextProvider = startupContextProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting container initialization service.");
            _cancellationToken = cancellationToken;

            // The service should be registered in Linux Consumption only, but do additional check here.
            if (_environment.IsAnyLinuxConsumption())
            {
                await ApplyStartContextIfPresent(cancellationToken);
            }
        }

        private async Task ApplyStartContextIfPresent(CancellationToken cancellationToken)
        {
            (bool hasStartContext, string startContext) = await TryGetStartContextOrNullAsync(cancellationToken);

            if (hasStartContext && !string.IsNullOrEmpty(startContext))
            {
                _logger.LogDebug("Applying host context");

                var encryptedAssignmentContext = JsonConvert.DeserializeObject<EncryptedHostAssignmentContext>(startContext);
                var assignmentContext = _startupContextProvider.SetContext(encryptedAssignmentContext);
                await SpecializeMSISideCar(assignmentContext);

                bool success = _instanceManager.StartAssignment(assignmentContext);
                _logger.LogDebug($"StartAssignment invoked (Success={success})");
            }
            else
            {
                _logger.LogDebug("No host context specified. Waiting for host assignment");
            }
        }

        protected abstract Task<(bool HasStartContext, string StartContext)> TryGetStartContextOrNullAsync(CancellationToken cancellationToken);

        protected abstract Task SpecializeMSISideCar(HostAssignmentContext assignmentContext);

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
