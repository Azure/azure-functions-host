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
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class InstanceManager : IInstanceManager
    {
        private static readonly object _assignmentLock = new object();
        private static HostAssignmentContext _assignmentContext;

        private readonly ILogger _logger;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly IRunFromPackageHandler _runFromPackageHandler;
        private readonly IPackageDownloadHandler _packageDownloadHandler;
        private readonly IEnvironment _environment;
        private readonly IOptionsFactory<ScriptApplicationHostOptions> _optionsFactory;
        private readonly HttpClient _client;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;

        public InstanceManager(IOptionsFactory<ScriptApplicationHostOptions> optionsFactory, IHttpClientFactory httpClientFactory, IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment, ILogger<InstanceManager> logger, IMetricsLogger metricsLogger, IMeshServiceClient meshServiceClient, IRunFromPackageHandler runFromPackageHandler,
            IPackageDownloadHandler packageDownloadHandler)
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

        public async Task<string> SpecializeMSISidecar(HostAssignmentContext context)
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
                if (context.MSIContext == null)
                {
                    _logger.LogWarning("Skipping specialization of MSI sidecar since MSIContext was absent");
                    await _meshServiceClient.NotifyHealthEvent(ContainerHealthEventType.Fatal, this.GetType(),
                        "Could not specialize MSI sidecar since MSIContext was empty");
                }
                else
                {
                    using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationMSIInit))
                    {
                        var uri = new Uri(endpoint);
                        var address = $"http://{uri.Host}:{uri.Port}{ScriptConstants.LinuxMSISpecializationStem}";

                        _logger.LogDebug($"Specializing sidecar at {address}");

                        var requestMessage = new HttpRequestMessage(HttpMethod.Post, address)
                        {
                            Content = new StringContent(JsonConvert.SerializeObject(context.MSIContext),
                                Encoding.UTF8, "application/json")
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

        public bool StartAssignment(HostAssignmentContext context)
        {
            if (!_webHostEnvironment.InStandbyMode)
            {
                // This is only true when specializing pinned containers.
                if (!context.Environment.TryGetValue(EnvironmentSettingNames.ContainerStartContext, out string startContext))
                {
                    _logger.LogError("Assign called while host is not in placeholder mode and start context is not present.");
                    return false;
                }
            }

            if (_environment.IsContainerReady())
            {
                _logger.LogError("Assign called while container is marked as specialized.");
                return false;
            }

            if (context.IsWarmupRequest)
            {
                // Based on profiling download code jit-ing holds up cold start.
                // Pre-jit to avoid paying the cost later.
                Task.Run(async () => await _packageDownloadHandler.Download(context.GetRunFromPkgContext()));
                return true;
            }
            else if (_assignmentContext == null)
            {
                lock (_assignmentLock)
                {
                    if (_assignmentContext != null)
                    {
                        return _assignmentContext.Equals(context);
                    }
                    _assignmentContext = context;
                }

                _logger.LogInformation($"Starting Assignment. Cloud Name: {_environment.GetCloudName()}");

                // set a flag which will cause any incoming http requests to buffer
                // until specialization is complete
                // the host is guaranteed not to receive any requests until AFTER assign
                // has been initiated, so setting this flag here is sufficient to ensure
                // that any subsequent incoming requests while the assign is in progress
                // will be delayed until complete
                _webHostEnvironment.DelayRequests();

                // start the specialization process in the background
                Task.Run(async () => await Assign(context));

                return true;
            }
            else
            {
                // No lock needed here since _assignmentContext is not null when we are here
                return _assignmentContext.Equals(context);
            }
        }

        public async Task<string> ValidateContext(HostAssignmentContext assignmentContext)
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

        private async Task<(string, long?)> ValidateBlobPackageContext(RunFromPackageContext context)
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
                await share.CreateIfNotExistsAsync();
                return null;
            }
            catch (Exception e)
            {
                _logger.LogError(e, nameof(ValidateAzureFilesContext));
                return e.Message;
            }
        }

        private async Task Assign(HostAssignmentContext assignmentContext)
        {
            try
            {
                // first make all environment and file system changes required for
                // the host to be specialized
                await ApplyContext(assignmentContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Assign failed");
                await _meshServiceClient.NotifyHealthEvent(ContainerHealthEventType.Fatal, GetType(), "Assign failed");
                throw;
            }
            finally
            {
                // all assignment settings/files have been applied so we can flip
                // the switch now on specialization
                // even if there are failures applying context above, we want to
                // leave placeholder mode
                _logger.LogInformation("Triggering specialization");
                _webHostEnvironment.FlagAsSpecializedAndReady();

                _webHostEnvironment.ResumeRequests();
            }
        }

        private async Task ApplyContext(HostAssignmentContext assignmentContext)
        {
            _logger.LogInformation($"Applying {assignmentContext.Environment.Count} app setting(s)");
            assignmentContext.ApplyAppSettings(_environment, _logger);

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

        public IDictionary<string, string> GetInstanceInfo()
        {
            return new Dictionary<string, string>
            {
                { "FUNCTIONS_EXTENSION_VERSION", ScriptHost.Version },
                { "WEBSITE_NODE_DEFAULT_VERSION", "8.5.0" }
            };
        }

        // for testing
        internal static void Reset()
        {
            _assignmentContext = null;
        }
    }
}
