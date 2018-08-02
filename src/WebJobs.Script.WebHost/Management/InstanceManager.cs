﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class InstanceManager : IInstanceManager
    {
        private static HostAssignmentContext _assignmentContext;
        private static object _assignmentLock = new object();

        private readonly ScriptWebHostOptions _webHostSettings;
        private readonly ILogger _logger;
        private readonly HttpClient _client;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;

        public InstanceManager(IOptions<ScriptWebHostOptions> webHostSettings, ILoggerFactory loggerFactory, HttpClient client, IScriptWebHostEnvironment webHostEnvironment)
        {
            if (webHostSettings == null)
            {
                throw new ArgumentNullException(nameof(webHostSettings));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _client = client ?? throw new ArgumentNullException(nameof(client));
            _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
            _webHostSettings = webHostSettings.Value;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
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

                // start the specialization process in the background
                _logger.LogInformation("Starting Assignment");
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
                // set a flag which will cause any incoming http requests to buffer
                // until specialization is complete
                // the host is guaranteed not to receive any requests until AFTER assign
                // has been initiated, so setting this flag here is sufficient to ensure
                // that any subsequent incoming requests while the assign is in progress
                // will be delayed until complete
                _webHostEnvironment.DelayRequests();

                // first make all environment and file system changes required for
                // the host to be specialized
                await ApplyContext(assignmentContext);

                // all assignment settings/files have been applied so we can flip
                // the switch now on specialization
                _logger.LogInformation("Triggering specialization");
                _webHostEnvironment.FlagAsSpecializedAndReady();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Assign failed");
                throw;
            }
            finally
            {
                _webHostEnvironment.ResumeRequests();
            }
        }

        private async Task ApplyContext(HostAssignmentContext assignmentContext)
        {
            _logger.LogInformation($"Applying {assignmentContext.Environment.Count} app setting(s)");
            assignmentContext.ApplyAppSettings();

            if (!string.IsNullOrEmpty(assignmentContext.ZipUrl))
            {
                // download zip and extract
                var zipUri = new Uri(assignmentContext.ZipUrl);
                var filePath = Path.GetTempFileName();
                await DownloadAsync(zipUri, filePath);

                _logger.LogInformation($"Extracting files to '{_webHostSettings.ScriptPath}'");
                ZipFile.ExtractToDirectory(filePath, _webHostSettings.ScriptPath, overwriteFiles: true);
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
    }
}
