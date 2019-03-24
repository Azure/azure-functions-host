// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class InstanceManager : IInstanceManager
    {
        private static readonly object _assignmentLock = new object();
        private static HostAssignmentContext _assignmentContext;

        private readonly ILogger _logger;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IEnvironment _environment;
        private readonly IOptionsFactory<ScriptApplicationHostOptions> _optionsFactory;
        private readonly HttpClient _client;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;

        public InstanceManager(IOptionsFactory<ScriptApplicationHostOptions> optionsFactory, HttpClient client, IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment, ILogger<InstanceManager> logger, IMetricsLogger metricsLogger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metricsLogger = metricsLogger;
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _optionsFactory = optionsFactory ?? throw new ArgumentNullException(nameof(optionsFactory));
        }

        public bool StartAssignment(HostAssignmentContext context)
        {
            if (!_webHostEnvironment.InStandbyMode)
            {
                _logger.LogError("Assign called while host is not in placeholder mode");
                return false;
            }

            if (_assignmentContext == null)
            {
                lock (_assignmentLock)
                {
                    if (_assignmentContext != null)
                    {
                        return _assignmentContext.Equals(context);
                    }
                    _assignmentContext = context;
                }

                _logger.LogInformation("Starting Assignment");

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
            _logger.LogInformation($"Validating host assignment context (SiteId: {assignmentContext.SiteId}, SiteName: '{assignmentContext.SiteName}')");

            string error = null;
            HttpResponseMessage response = null;
            try
            {
                var zipUrl = assignmentContext.ZipUrl;
                if (!string.IsNullOrEmpty(zipUrl))
                {
                    // make sure the zip uri is valid and accessible
                    await Utility.InvokeWithRetriesAsync(async () =>
                    {
                        try
                        {
                            using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationZipHead))
                            {
                                var request = new HttpRequestMessage(HttpMethod.Head, zipUrl);
                                response = await _client.SendAsync(request);
                                response.EnsureSuccessStatusCode();
                            }
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, $"{MetricEventNames.LinuxContainerSpecializationZipHead} failed");
                            throw;
                        }
                    }, maxRetries: 2, retryInterval: TimeSpan.FromSeconds(0.3)); // Keep this less than ~1s total
                }
            }
            catch (Exception e)
            {
                error = $"Invalid zip url specified (StatusCode: {response?.StatusCode})";
                _logger.LogError(e, "ValidateContext failed");
            }

            return error;
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
                _logger.LogError(ex, $"Assign failed");
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
            assignmentContext.ApplyAppSettings(_environment);

            // We need to get the non-PlaceholderMode script path so we can unzip to the correct location.
            // This asks the factory to skip the PlaceholderMode check when configuring options.
            var options = _optionsFactory.Create(ScriptApplicationHostOptionsSetup.SkipPlaceholder);

            var zipPath = assignmentContext.ZipUrl;
            if (!string.IsNullOrEmpty(zipPath))
            {
                // download zip and extract
                var zipUri = new Uri(zipPath);
                var filePath = Path.GetTempFileName();
                await DownloadAsync(zipUri, filePath);

                using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationZipExtract))
                {
                    _logger.LogInformation($"Extracting files to '{options.ScriptPath}'");
                    ZipFile.ExtractToDirectory(filePath, options.ScriptPath, overwriteFiles: true);
                    _logger.LogInformation($"Zip extraction complete");
                }
            }
        }

        private async Task DownloadAsync(Uri zipUri, string filePath)
        {
            string cleanedUrl;
            Utility.TryCleanUrl(zipUri.AbsoluteUri, out cleanedUrl);

            _logger.LogInformation($"Downloading zip contents from '{cleanedUrl}' to temp file '{filePath}'");

            HttpResponseMessage response = null;

            await Utility.InvokeWithRetriesAsync(async () =>
            {
                try
                {
                    using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationZipDownload))
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, zipUri);
                        response = await _client.SendAsync(request);
                        response.EnsureSuccessStatusCode();
                    }
                }
                catch (Exception e)
                {
                    string error = $"Error downloading zip content {cleanedUrl}";
                    _logger.LogError(e, error);
                    throw;
                }

                _logger.LogInformation($"{response.Content.Headers.ContentLength} bytes downloaded");
            }, 2, TimeSpan.FromSeconds(0.5));

            using (_metricsLogger.LatencyEvent(MetricEventNames.LinuxContainerSpecializationZipWrite))
            {
                using (var content = await response.Content.ReadAsStreamAsync())
                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    await content.CopyToAsync(stream);
                }

                _logger.LogInformation($"{response.Content.Headers.ContentLength} bytes written");
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
