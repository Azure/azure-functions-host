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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement
{
    public class LinuxContainerInitializationHostService : IHostedService
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
            if (_environment.IsLinuxConsumptionOnAtlas())
            {
                await ApplyStartContextIfPresent();
            }
            else if (_environment.IsLinuxConsumptionOnLegion())
            {
                await ApplyStartContextIfPresent2();
                _logger.LogInformation("Container has (re)started. Waiting for specialization");
            }
        }

        private async Task ApplyStartContextIfPresent()
        {
            var startContext = await GetStartContextOrNullAsync();

            if (!string.IsNullOrEmpty(startContext))
            {
                _logger.LogInformation("Applying host context");
                _logger.LogInformation($"[TEST][HOST] startContext: {startContext}");

                var encryptedAssignmentContext = JsonConvert.DeserializeObject<EncryptedHostAssignmentContext>(startContext);
                var assignmentContext = _startupContextProvider.SetContext(encryptedAssignmentContext);

                var msiError = await _instanceManager.SpecializeMSISidecar(assignmentContext);
                if (!string.IsNullOrEmpty(msiError))
                {
                    // Log and continue specializing even in case of failures.
                    // There will be other mechanisms to recover the container.
                    _logger.LogError("MSI Specialization failed with '{msiError}'", msiError);
                }

                bool success = _instanceManager.StartAssignment(assignmentContext);
                _logger.LogInformation($"StartAssignment invoked (Success={success})");
            }
            else
            {
                _logger.LogInformation("No host context specified. Waiting for host assignment");
            }
        }

        private async Task ApplyStartContextIfPresent2()
        {
            var startContext = GetStartContextOrNullAsync2();

            if (!string.IsNullOrEmpty(startContext))
            {
                _logger.LogInformation("Applying host context");

                var encryptedAssignmentContext = JsonConvert.DeserializeObject<EncryptedHostAssignmentContext>(startContext);
                var assignmentContext = _startupContextProvider.SetContext(encryptedAssignmentContext);

                var msiError = await _instanceManager.SpecializeMSISidecar(assignmentContext);
                if (!string.IsNullOrEmpty(msiError))
                {
                    // Log and continue specializing even in case of failures.
                    // There will be other mechanisms to recover the container.
                    _logger.LogError("MSI Specialization failed with '{msiError}'", msiError);
                }

                bool success = _instanceManager.StartAssignment(assignmentContext);
                _logger.LogInformation($"StartAssignment invoked (Success={success})");
            }
            else
            {
                _logger.LogInformation("No host context specified. Waiting for host assignment");
            }
        }

        private async Task<string> GetStartContextOrNullAsync()
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

            return startContext;
        }

        private string GetStartContextOrNullAsync2()
        {
            _logger.LogInformation("GetStartContextOrNullAsync2");
            // read files in path: /CONTAINER_SPECIALIZATION_CONTEXT_MOUNT_PATH/Context.txt

            string path = Directory.GetCurrentDirectory();
            _logger.LogInformation($"[TEST][HOST] pwd: {path}");

            try
            {
                DirectoryInfo di = new DirectoryInfo(".");
                foreach (DirectoryInfo file in di.GetDirectories())
                {
                    _logger.LogInformation($"[TEST][HOST] . base directory Directory: {file.Name}");
                }
            }
            catch (Exception e)
            {
                _logger.LogInformation($"[TEST][HOST] 1 {e.ToString()}");
            }

            try
            {
                DirectoryInfo dir = new DirectoryInfo("/");
                var logoutput = "[TEST][HOST] / base directory Directory: ";
                foreach (DirectoryInfo file in dir.GetDirectories())
                {
                    logoutput += file.Name;
                }
                _logger.LogInformation(logoutput);
            }
            catch (Exception e)
            {
                _logger.LogInformation($"[TEST][HOST] 2 {e.ToString()}");
            }

            try
            {
                if (Directory.Exists("/mnt"))
                {
                    _logger.LogInformation("[TEST][HOST] /mnt Directory exists");
                    string[] filePaths2 = Directory.GetFiles("/mnt");
                    _logger.LogInformation("[TEST][HOST] GetStartContextOrNullAsync2 1");
                    foreach (string filePath2 in filePaths2)
                    {
                        _logger.LogInformation("[TEST][HOST] file exists");
                        try
                        {
                            _logger.LogInformation($"[TEST][HOST] file: {Path.GetFileName(filePath2)}");
                        }
                        catch (Exception e)
                        {
                            _logger.LogInformation($"[TEST][HOST] 3.5 {e.ToString()}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogInformation($"[TEST][HOST] 3 {e.ToString()}");
            }

            try
            {
                if (Directory.Exists("/container-specialization-context"))
                {
                    _logger.LogInformation("[TEST][HOST] /container-specialization-context Directory exists");
                    string[] filePaths3 = Directory.GetFiles("/container-specialization-context");
                    _logger.LogInformation("[TEST][HOST] GetStartContextOrNullAsync2 2");
                    foreach (string filePath2 in filePaths3)
                    {
                        _logger.LogInformation("[TEST][HOST] /container-specialization-context file exists");
                        try
                        {
                            _logger.LogInformation($"[TEST][HOST] /container-specialization-context file: {Path.GetFileName(filePath2)}");
                        }
                        catch (Exception e)
                        {
                            _logger.LogInformation($"[TEST][HOST] 4.5 {e.ToString()}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogInformation($"[TEST][HOST] 4 {e.ToString()}");
            }

            //while (true)
            //{
            //    _logger.LogInformation("[TEST][HOST] Checking context path");
            //    var contextFile = "/container-specialization-context/Context.txt";
            //    if (Directory.Exists("/container-specialization-context"))
            //    {
            //        _logger.LogInformation("[TEST][HOST] /container-specialization-context Directory exists");
            //        string[] filePaths3 = Directory.GetFiles("/container-specialization-context");
            //        _logger.LogInformation("[TEST][HOST] GetStartContextOrNullAsync2 2");
            //        foreach (string file in filePaths3)
            //        {
            //            try
            //            {
            //                _logger.LogInformation($"[TEST][HOST] /container-specialization-context file: {Path.GetFileName(file)}");
            //            }
            //            catch (Exception e)
            //            {
            //                _logger.LogInformation($"[TEST][HOST] 4.5 {e.ToString()}");
            //            }
            //        }

            //        if (File.Exists(contextFile))
            //        {
            //            _logger.LogInformation($"[TEST][HOST] The file exists!!!");
            //            string contents = File.ReadAllText(contextFile);
            //            _logger.LogInformation($"[TEST][HOST] {contents}");
            //            break;
            //        }
            //        else
            //        {
            //            _logger.LogInformation($"[TEST][HOST] The file {contextFile} does not exist.");
            //        }
            //    }
            //    Thread.Sleep(30000);
            //}
            //Thread.Sleep(30000);
            //_logger.LogInformation($"[TEST][HOST] Fake Assign here");
            return string.Empty;

            // var startContext = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerStartContext);

            // // Container start context is not available directly
            // if (string.IsNullOrEmpty(startContext))
            // {
            //     // Check if the context is available in blob
            //     var sasUri = _environment.GetEnvironmentVariable(EnvironmentSettingNames.ContainerStartContextSasUri);

            //     if (!string.IsNullOrEmpty(sasUri))
            //     {
            //         _logger.LogInformation("Host context specified via CONTAINER_START_CONTEXT_SAS_URI");
            //         startContext = await GetAssignmentContextFromSasUri(sasUri);
            //     }
            // }
            // else
            // {
            //     _logger.LogInformation("Host context specified via CONTAINER_START_CONTEXT");
            // }

            // return startContext;
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

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
