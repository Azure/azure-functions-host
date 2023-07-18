// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class AtlasContainerInitializationHostedService : LinuxContainerInitializationHostedService
    {
        private readonly IEnvironment _environment;
        private readonly IInstanceManager _instanceManager;
        private readonly ILogger _logger;
        private readonly StartupContextProvider _startupContextProvider;
        private CancellationToken _cancellationToken;

        public AtlasContainerInitializationHostedService(IEnvironment environment, IInstanceManager instanceManager,
            ILogger<AtlasContainerInitializationHostedService> logger, StartupContextProvider startupContextProvider)
            : base(environment, instanceManager, logger, startupContextProvider)
        {
            _environment = environment;
            _instanceManager = instanceManager;
            _logger = logger;
            _startupContextProvider = startupContextProvider;
        }

        protected override async Task<(bool HasStartContext, string StartContext)> TryGetStartContextOrNullAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            var startContext = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerStartContext);

            // Container start context is not available directly
            if (string.IsNullOrEmpty(startContext))
            {
                // Check if the context is available in blob
                var sasUri = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerStartContextSasUri);

                if (!string.IsNullOrEmpty(sasUri))
                {
                    _logger.LogDebug($"Host context specified via {EnvironmentSettingNames.ContainerStartContextSasUri}");
                    startContext = await GetAssignmentContextFromSasUri(sasUri);
                }
            }
            else
            {
                _logger.LogDebug($"Host context specified via {EnvironmentSettingNames.ContainerStartContext}");
            }

            return (!string.IsNullOrEmpty(startContext), startContext);
        }

        protected override async Task SpecializeMSISideCar(HostAssignmentContext assignmentContext)
        {
            var msiError = await _instanceManager.SpecializeMSISidecar(assignmentContext);
            if (!string.IsNullOrEmpty(msiError))
            {
                // Log and continue specializing even in case of failures.
                // There will be other mechanisms to recover the container.
                _logger.LogError("MSI Specialization failed with '{msiError}'", msiError);
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
            var blobClientOptions = new BlobClientOptions();
            blobClientOptions.Retry.Mode = RetryMode.Fixed;
            blobClientOptions.Retry.MaxRetries = 3;
            blobClientOptions.Retry.Delay = TimeSpan.FromMilliseconds(500);

            var blobClient = new BlobClient(new Uri(uri), blobClientOptions);

            if (await blobClient.ExistsAsync(cancellationToken: _cancellationToken))
            {
                var downloadResponse = await blobClient.DownloadAsync(cancellationToken: _cancellationToken);
                using (StreamReader reader = new StreamReader(downloadResponse.Value.Content, true))
                {
                    string content = reader.ReadToEnd();
                    return content;
                }
            }

            return string.Empty;
        }
    }
}
