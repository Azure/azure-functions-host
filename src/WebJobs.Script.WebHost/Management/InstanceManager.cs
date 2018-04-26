// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class InstanceManager : IInstanceManager
    {
        private static HostAssignmentContext _assignmentContext;
        private static object _assignmentLock = new object();

        private readonly WebHostSettings _webHostSettings;
        private readonly ILogger _logger;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly HttpClient _client;

        public InstanceManager(ScriptSettingsManager settingsManager, WebHostSettings webHostSettings, ILoggerFactory loggerFactory, HttpClient client)
        {
            _settingsManager = settingsManager;
            _webHostSettings = webHostSettings;
            _logger = loggerFactory.CreateLogger(nameof(InstanceManager));
            _client = client;
        }

        public bool StartAssignment(HostAssignmentContext assignmentContext)
        {
            if (!WebScriptHostManager.InStandbyMode)
            {
                return false;
            }

            if (_assignmentContext == null)
            {
                lock (_assignmentLock)
                {
                    if (_assignmentContext != null)
                    {
                        return _assignmentContext.Equals(assignmentContext);
                    }
                    _assignmentContext = assignmentContext;
                }

                // Queue the task in background, and return to caller immediately
                Task.Run(() => Specialize(assignmentContext));

                return true;
            }
            else
            {
                // No lock needed here since _assignmentContext is not null when we are here
                return _assignmentContext.Equals(assignmentContext);
            }
        }

        private async Task Specialize(HostAssignmentContext assignmentContext)
        {
            try
            {
                var zip = assignmentContext.ZipUrl;
                if (!string.IsNullOrEmpty(zip))
                {
                    // download zip and extract
                    var filePath = Path.GetTempFileName();

                    await DownloadAsync(new Uri(zip), filePath);

                    assignmentContext.ApplyAppSettings();

                    ZipFile.ExtractToDirectory(filePath, _webHostSettings.ScriptPath, overwriteFiles: true);
                }
                else
                {
                    assignmentContext.ApplyAppSettings();
                }

                // set flags which will trigger specialization
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
                _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error calling {nameof(Specialize)}");
            }
        }

        private async Task DownloadAsync(Uri requestUri, string filePath)
        {
            var response = await _client.GetAsync(requestUri);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidDataException($"Error downloading zip content {requestUri.Authority}/{requestUri.AbsolutePath}");
            }

            using (var content = await response.Content.ReadAsStreamAsync())
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
            {
                await content.CopyToAsync(stream);
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
    }
}
