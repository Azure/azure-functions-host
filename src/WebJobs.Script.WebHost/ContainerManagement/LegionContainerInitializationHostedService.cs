// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class LegionContainerInitializationHostedService : LinuxContainerInitializationHostedService
    {
        private const string ContextFile = "context.txt";

        private readonly IEnvironment _environment;
        private readonly ILogger _logger;

        public LegionContainerInitializationHostedService(IEnvironment environment, IInstanceManager instanceManager,
            ILogger<LegionContainerInitializationHostedService> logger, StartupContextProvider startupContextProvider)
            : base(environment, instanceManager, logger, startupContextProvider)
        {
            _environment = environment;
            _logger = logger;
        }

        protected override Task<(bool HasStartContext, string StartContext)> TryGetStartContextOrNullAsync(CancellationToken cancellationToken)
        {
            string containerSpecializationContextMountPath = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerSpecializationContextVolumePath);

            // The CONTAINER_SPECIALIZATION_CONTEXT_MOUNT_PATH environment variable should be set during pod creation
            if (string.IsNullOrEmpty(containerSpecializationContextMountPath))
            {
                _logger.LogError($"{EnvironmentSettingNames.ContainerSpecializationContextVolumePath} is Null or Empty");
                return Task.FromResult((false, string.Empty));
            }

            // The CONTAINER_SPECIALIZATION_CONTEXT_MOUNT_PATH emptyDir volume should be mounted by Legion during pod creation
            if (!Directory.Exists(containerSpecializationContextMountPath))
            {
                _logger.LogError("Container specialization context mount does not exist");
                return Task.FromResult((false, string.Empty));
            }

            string contextFilePath = Path.Combine(containerSpecializationContextMountPath, ContextFile);

            if (File.Exists(contextFilePath))
            {
                _logger.LogDebug($"Previous start context found");
                try
                {
                    var startContext = File.ReadAllText(contextFilePath);
                    return Task.FromResult((true, startContext));
                }
                catch (Exception e)
                {
                    _logger.LogError($"Error reading previous start context: {e.ToString()}");
                }
            }

            return Task.FromResult((false, string.Empty));
        }

        // No-op
        protected override Task SpecializeMSISideCar(HostAssignmentContext assignmentContext)
        {
            return Task.CompletedTask;
        }
    }
}
