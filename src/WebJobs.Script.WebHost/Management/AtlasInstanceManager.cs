// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.File;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class AtlasInstanceManager : LinuxInstanceManager
    {
        private readonly object _assignmentLock = new object();

        private readonly ILogger _logger;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly IRunFromPackageHandler _runFromPackageHandler;
        private readonly IPackageDownloadHandler _packageDownloadHandler;
        private readonly IEnvironment _environment;
        private readonly IOptionsFactory<ScriptApplicationHostOptions> _optionsFactory;
        private readonly HttpClient _client;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;

        public AtlasInstanceManager(IOptionsFactory<ScriptApplicationHostOptions> optionsFactory, IHttpClientFactory httpClientFactory, IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment, ILogger<AtlasInstanceManager> logger, IMetricsLogger metricsLogger, IMeshServiceClient meshServiceClient, IRunFromPackageHandler runFromPackageHandler,
            IPackageDownloadHandler packageDownloadHandler) : base(httpClientFactory, webHostEnvironment,
            environment, logger, metricsLogger, meshServiceClient)
        {
            _client = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metricsLogger = metricsLogger;
            _meshServiceClient = meshServiceClient;
            _runFromPackageHandler = runFromPackageHandler ?? throw new ArgumentNullException(nameof(runFromPackageHandler));
            _packageDownloadHandler = packageDownloadHandler ?? throw new ArgumentNullException(nameof(packageDownloadHandler));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _optionsFactory = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
        }

        public override async Task<string> SpecializeMSISidecar(HostAssignmentContext context)
        {
            // No cold start optimization needed for side car scenarios
            if (context.IsWarmupRequest)
            {
                return null;
            }

            var msiEnabled = context.IsMSIEnabled(out var endpoint);

            _logger.LogInformation($"MSI enabled status: {msiEnabled}");

            if (msiEnabled)
            {
                if (context.MSIContext == null && context.EncryptedTokenServiceSpecializationPayload == null)
                {
                    _logger.LogWarning("Skipping specialization of MSI sidecar since MSIContext and EncryptedTokenServiceSpecializationPayload were absent");
                    await _meshServiceClient.NotifyHealthEvent(ContainerHealthEventType.Fatal, this.GetType(),
                        "Could not specialize MSI sidecar since MSIContext and EncryptedTokenServiceSpecializationPayload were empty");
                }
                else
                {
                    using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationMSIInit))
                    {
                        var uri = new Uri(endpoint);
                        var addressStem = GetMsiSpecializationRequestAddressStem(context);

                        var address = $"http://{uri.Host}:{uri.Port}{addressStem}";
                        _logger.LogDebug($"Specializing sidecar at {address}");

                        StringContent payload;
                        if (string.IsNullOrEmpty(context.EncryptedTokenServiceSpecializationPayload))
                        {
                            payload = new StringContent(JsonConvert.SerializeObject(context.MSIContext),
                                    Encoding.UTF8, "application/json");
                        }
                        else
                        {
                            payload = new StringContent(context.EncryptedTokenServiceSpecializationPayload, Encoding.UTF8);
                        }

                        var requestMessage = new HttpRequestMessage(HttpMethod.Post, address)
                        {
                            Content = payload
                        };

                        var response = await _client.SendAsync(requestMessage);

                        _logger.LogInformation($"Specialize MSI sidecar returned {response.StatusCode}");

                        if (!response.IsSuccessStatusCode)
                        {
                            var message = $"Specialize MSI sidecar call failed. StatusCode={response.StatusCode}";
                            _logger.LogError(message);
                            await _meshServiceClient.NotifyHealthEvent(ContainerHealthEventType.Fatal, this.GetType(),
                                "Failed to specialize MSI sidecar");
                            return message;
                        }
                    }
                }
            }

            return null;
        }

        public override async Task<string> ValidateContext(HostAssignmentContext assignmentContext)
        {
            _logger.LogInformation($"Validating host assignment context (SiteId: {assignmentContext.SiteId}, SiteName: '{assignmentContext.SiteName}'. IsWarmup: '{assignmentContext.IsWarmupRequest}')");
            RunFromPackageContext pkgContext = assignmentContext.GetRunFromPkgContext();
            _logger.LogInformation($"Will be using {pkgContext.EnvironmentVariableName} app setting as zip url. IsWarmup: '{assignmentContext.IsWarmupRequest}')");

            if (pkgContext.IsScmRunFromPackage())
            {
                // Not user assigned so limit validation
                return null;
            }
            else if (!string.IsNullOrEmpty(pkgContext.Url) && pkgContext.Url != "1")
            {
                if (Uri.TryCreate(pkgContext.Url, UriKind.Absolute, out var uri))
                {
                    if (Utility.IsResourceAzureBlobWithoutSas(uri))
                    {
                        // Note: this also means we skip validation for publicly available blobs
                        _logger.LogDebug("Skipping validation for '{pkgContext.EnvironmentVariableName}' with no SAS token", pkgContext.EnvironmentVariableName);
                        return null;
                    }
                    else
                    {
                        // In AppService, ZipUrl == 1 means the package is hosted in azure files.
                        // Otherwise we expect zipUrl to be a blobUri to a zip or a squashfs image
                        var (error, contentLength) = await ValidateBlobPackageContext(pkgContext);
                        if (string.IsNullOrEmpty(error))
                        {
                            assignmentContext.PackageContentLength = contentLength;
                        }
                        return error;
                    }
                }
                else
                {
                    var invalidUrlError = $"Invalid url for specified for {pkgContext.EnvironmentVariableName}";
                    _logger.LogError(invalidUrlError);
                    // For now we return null here instead of the actual error since this validation is new.
                    // Eventually this could return the error message.
                    return null;
                }
            }
            else if (!string.IsNullOrEmpty(assignmentContext.AzureFilesConnectionString))
            {
                return await ValidateAzureFilesContext(assignmentContext.AzureFilesConnectionString, assignmentContext.AzureFilesContentShare);
            }
            else
            {
                _logger.LogError("Missing ZipUrl and AzureFiles config. Continue with empty root.");
                return null;
            }
        }

        protected override async Task<string> DownloadWarmupAsync(RunFromPackageContext context)
        {
            return await _packageDownloadHandler.Download(context);
        }

        private async Task<(string Error, long? ContentLength)> ValidateBlobPackageContext(RunFromPackageContext context)
        {
            string blobUri = context.Url;
            string eventName = context.IsWarmUpRequest
                ? MetricEventNames.LinuxContainerSpecializationZipHeadWarmup
                : MetricEventNames.LinuxContainerSpecializationZipHead;
            string error = null;
            HttpResponseMessage response = null;
            long? contentLength = null;
            try
            {
                if (!string.IsNullOrEmpty(blobUri))
                {
                    // make sure the zip uri is valid and accessible
                    await Utility.InvokeWithRetriesAsync(async () =>
                    {
                        try
                        {
                            using (_metricsLogger.LatencyEvent(eventName))
                            {
                                var request = new HttpRequestMessage(HttpMethod.Head, blobUri);
                                response = await _client.SendAsync(request);
                                response.EnsureSuccessStatusCode();
                                if (response.Content != null && response.Content.Headers != null)
                                {
                                    contentLength = response.Content.Headers.ContentLength;
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, $"{eventName} failed");
                            throw;
                        }
                    }, maxRetries: 2, retryInterval: TimeSpan.FromSeconds(0.3)); // Keep this less than ~1s total
                }
            }
            catch (Exception e)
            {
                error = $"Invalid zip url specified (StatusCode: {response?.StatusCode})";
                _logger.LogError(e, $"ValidateContext failed. IsWarmupRequest = {context.IsWarmUpRequest}");
            }

            return (error, contentLength);
        }

        private async Task<string> ValidateAzureFilesContext(string connectionString, string contentShare)
        {
            try
            {
                var storageAccount = CloudStorageAccount.Parse(connectionString);
                var fileClient = storageAccount.CreateCloudFileClient();
                var share = fileClient.GetShareReference(contentShare);

                if (!share.Exists())
                {
                    await share.CreateIfNotExistsAsync();
                }

                return null;
            }
            catch (Exception e)
            {
                _logger.LogError(e, nameof(ValidateAzureFilesContext));
                return e.Message;
            }
        }

        protected override async Task ApplyContextAsync(HostAssignmentContext assignmentContext)
        {
            // We need to get the non-PlaceholderMode script Path so we can unzip to the correct location.
            // This asks the factory to skip the PlaceholderMode check when configuring options.
            var options = _optionsFactory.Create(ScriptApplicationHostOptionsSetup.SkipPlaceholder);
            RunFromPackageContext pkgContext = assignmentContext.GetRunFromPkgContext();

            if (_environment.SupportsAzureFileShareMount() || pkgContext.IsRunFromLocalPackage())
            {
                var azureFilesMounted = false;
                if (assignmentContext.IsAzureFilesContentShareConfigured(_logger))
                {
                    azureFilesMounted = await _runFromPackageHandler.MountAzureFileShare(assignmentContext);
                }
                else
                {
                    _logger.LogError(
                        $"No {nameof(EnvironmentSettingNames.AzureFilesConnectionString)} or {nameof(EnvironmentSettingNames.AzureFilesContentShare)} configured. Azure FileShare will not be mounted. For PowerShell Functions, Managed Dependencies will not persisted across functions host instances.");
                }

                if (pkgContext.IsRunFromPackage(options, _logger))
                {
                    if (azureFilesMounted)
                    {
                        _logger.LogWarning("App is configured to use both Run-From-Package and AzureFiles. Run-From-Package will take precedence");
                    }
                    var blobContextApplied =
                        await _runFromPackageHandler.ApplyRunFromPackageContext(pkgContext, options.ScriptPath,
                            azureFilesMounted, false);

                    if (!blobContextApplied && azureFilesMounted)
                    {
                        _logger.LogWarning($"Failed to {nameof(_runFromPackageHandler.ApplyRunFromPackageContext)}. Attempting to use local disk instead");
                        await _runFromPackageHandler.ApplyRunFromPackageContext(pkgContext, options.ScriptPath, false);
                    }
                }
                else if (pkgContext.IsRunFromLocalPackage())
                {
                    if (!azureFilesMounted)
                    {
                        const string mountErrorMessage = "App Run-From-Package is set as '1'. AzureFiles is needed but is not configured.";
                        _logger.LogWarning(mountErrorMessage);
                        throw new Exception(mountErrorMessage);
                    }

                    var blobContextApplied =
                        await _runFromPackageHandler.ApplyRunFromPackageContext(pkgContext, options.ScriptPath, azureFilesMounted);

                    if (!blobContextApplied)
                    {
                        _logger.LogWarning($"Failed to {nameof(_runFromPackageHandler.ApplyRunFromPackageContext)}.");
                    }
                }
                else
                {
                    _logger.LogInformation($"No {nameof(EnvironmentSettingNames.AzureWebsiteRunFromPackage)} configured");
                }
            }
            else
            {
                if (pkgContext.IsRunFromPackage(options, _logger))
                {
                    await _runFromPackageHandler.ApplyRunFromPackageContext(pkgContext, options.ScriptPath, false);
                }
                else if (assignmentContext.IsAzureFilesContentShareConfigured(_logger))
                {
                    await _runFromPackageHandler.MountAzureFileShare(assignmentContext);
                }
            }

            // BYOS
            var storageVolumes = assignmentContext.GetBYOSEnvironmentVariables()
                .Select(AzureStorageInfoValue.FromEnvironmentVariable).ToList();

            var mountedVolumes =
                (await Task.WhenAll(storageVolumes.Where(v => v != null).Select(MountStorageAccount))).Where(
                    result => result).ToList();

            if (storageVolumes.Any())
            {
                if (mountedVolumes.Count != storageVolumes.Count)
                {
                    _logger.LogWarning(
                        $"Successfully mounted {mountedVolumes.Count} / {storageVolumes.Count} BYOS storage accounts");
                }
                else
                {
                    _logger.LogInformation(
                        $"Successfully mounted {storageVolumes.Count} BYOS storage accounts");
                }
            }
        }

        private async Task<bool> MountStorageAccount(AzureStorageInfoValue storageInfoValue)
        {
            try
            {
                var storageConnectionString =
                    Utility.BuildStorageConnectionString(storageInfoValue.AccountName, storageInfoValue.AccessKey, _environment.GetStorageSuffix());

                await Utility.InvokeWithRetriesAsync(async () =>
                {
                    try
                    {
                        using (_metricsLogger.LatencyEvent($"{MetricEventNames.LinuxContainerSpecializationBYOSMountPrefix}.{storageInfoValue.Type.ToString().ToLowerInvariant()}.{storageInfoValue.Id?.ToLowerInvariant()}"))
                        {
                            switch (storageInfoValue.Type)
                            {
                                case AzureStorageType.AzureFiles:
                                    if (!await _meshServiceClient.MountCifs(storageConnectionString, storageInfoValue.ShareName, storageInfoValue.MountPath))
                                    {
                                        throw new Exception($"Failed to mount BYOS fileshare {storageInfoValue.Id}");
                                    }
                                    break;
                                case AzureStorageType.AzureBlob:
                                    await _meshServiceClient.MountBlob(storageConnectionString, storageInfoValue.ShareName, storageInfoValue.MountPath);
                                    break;
                                default:
                                    throw new NotSupportedException($"Unknown BYOS storage type {storageInfoValue.Type}");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // todo: Expose any failures here as a part of a health check api.
                        _logger.LogError(e, $"Failed to mount BYOS storage account {storageInfoValue.Id}");
                        throw;
                    }
                    _logger.LogInformation(
                        $"Successfully mounted BYOS storage account {storageInfoValue.Id}");
                }, 1, TimeSpan.FromSeconds(0.5));

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to mount BYOS storage account {storageInfoValue.Id}");
                return false;
            }
        }

        private string GetMsiSpecializationRequestAddressStem(HostAssignmentContext context)
        {
            var stem = ScriptConstants.LinuxMSISpecializationStem;

            if (!string.IsNullOrEmpty(context.EncryptedTokenServiceSpecializationPayload))
            {
                _logger.LogDebug("Using encrypted TokenService payload format");

                // use default encrypted API endpoint if endpoint not provided in context
                stem = string.IsNullOrEmpty(context.TokenServiceApiEndpoint)
                    ? ScriptConstants.LinuxEncryptedTokenServiceSpecializationStem
                    : context.TokenServiceApiEndpoint;
            }

            return stem;
        }
    }
}
