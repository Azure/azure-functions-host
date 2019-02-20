// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
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
        private readonly IEnvironment _environment;
        private readonly IOptionsFactory<ScriptApplicationHostOptions> _optionsFactory;
        private readonly HttpClient _client;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;

        public InstanceManager(IOptionsFactory<ScriptApplicationHostOptions> optionsFactory, HttpClient client, IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment, ILogger<InstanceManager> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            var zipUrl = assignmentContext.ZipUrl;
            if (!string.IsNullOrEmpty(zipUrl))
            {
                // make sure the zip uri is valid and accessible
                var request = new HttpRequestMessage(HttpMethod.Head, zipUrl);
                var response = await _client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string error = $"Invalid zip url specified (StatusCode: {response.StatusCode})";
                    _logger.LogError(error);
                    return error;
                }
            }

            return null;
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

                _logger.LogInformation($"Extracting files to '{options.ScriptPath}'");
                ZipFile.ExtractToDirectory(filePath, options.ScriptPath, overwriteFiles: true);
                _logger.LogInformation($"Zip extraction complete");
            }
        }

        private async Task DownloadAsync(Uri zipUri, string filePath)
        {
            var zipPath = $"{zipUri.Authority}{zipUri.AbsolutePath}";
            _logger.LogInformation($"Downloading zip contents from '{zipPath}' to temp file '{filePath}'");

            var response = await _client.GetAsync(zipUri);
            if (!response.IsSuccessStatusCode)
            {
                string error = $"Error downloading zip content {zipPath}";
                _logger.LogError(error);
                throw new InvalidDataException(error);
            }

            using (var content = await response.Content.ReadAsStreamAsync())
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await content.CopyToAsync(stream);
            }

            _logger.LogInformation($"{response.Content.Headers.ContentLength} bytes downloaded");
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
