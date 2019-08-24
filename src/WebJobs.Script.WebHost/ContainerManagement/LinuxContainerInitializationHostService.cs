// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class LinuxContainerInitializationHostService : IHostedService
    {
        private readonly IEnvironment _environment;
        private readonly IInstanceManager _instanceManager;
        private readonly ILogger _logger;
        private CancellationToken _cancellationToken;

        public LinuxContainerInitializationHostService(IEnvironment environment, IInstanceManager instanceManager, ILogger<LinuxContainerInitializationHostService> logger)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing LinuxContainerInitializationService.");
            _cancellationToken = cancellationToken;

            // The service should be registered in IsLinuxContainerEnvironment only. But do additional check here.
            if (_environment.IsLinuxContainerEnvironment())
            {
                await ApplyContextIfPresent();
            }
        }

        private async Task ApplyContextIfPresent()
        {
            var startContext = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerStartContext);

            // Container start context is not available directly
            if (string.IsNullOrEmpty(startContext))
            {
                // Check if the context is available in blob
                var sasUri = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerStartContextSasUri);

                if (!string.IsNullOrEmpty(sasUri))
                {
                    _logger.LogInformation("Host context specified via CONTAINER_START_CONTEXT_SAS_URI");
                    startContext = await GetAssignmentContextFromSasUri(sasUri);
                }
            }
            else
            {
                _logger.LogInformation("Host context specified via CONTAINER_START_CONTEXT");
            }

            if (!string.IsNullOrEmpty(startContext))
            {
                _logger.LogInformation("Applying host context");

                var encryptedAssignmentContext = JsonConvert.DeserializeObject<EncryptedHostAssignmentContext>(startContext);
                var containerKey = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerEncryptionKey);
                var assignmentContext = encryptedAssignmentContext.Decrypt(containerKey);
                if (_instanceManager.StartAssignment(assignmentContext, false))
                {
                    _logger.LogInformation("Start assign HostAssignmentContext success");
                }
                else
                {
                    _logger.LogError("Start assign HostAssignmentContext failed");
                }
            }
            else
            {
                _logger.LogInformation("No host context specified. Waiting for host assignment");
            }
        }

        private async Task<string> GetAssignmentContextFromSasUri(string sasUri)
        {
            try
            {
                return await Read(sasUri);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error calling {nameof(GetAssignmentContextFromSasUri)}");
            }

            return string.Empty;
        }

        // virtual for unit testing
        public virtual async Task<string> Read(string uri)
        {
            // Note: ContainerStartContextSasUri will always be available for Zip based containers.
            // But the blob pointed to by the uri will not exist until the container is specialized.
            // When the blob doesn't exist it just means the container is waiting for specialization.
            // Don't treat this as a failure.
            var cloudBlockBlob = new CloudBlockBlob(new Uri(uri));

            var blobRequestOptions = new BlobRequestOptions
            {
                RetryPolicy = new LinearRetry(TimeSpan.FromMilliseconds(500), 3)
            };
            if (await cloudBlockBlob.ExistsAsync(blobRequestOptions, null, _cancellationToken))
            {
                return await cloudBlockBlob.DownloadTextAsync(null, null, blobRequestOptions, null, _cancellationToken);
            }

            return string.Empty;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
