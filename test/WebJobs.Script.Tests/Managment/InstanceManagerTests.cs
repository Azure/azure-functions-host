// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class InstanceManagerTests : IDisposable
    {
        private readonly TestLoggerProvider _loggerProvider;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly InstanceManager _instanceManager;
        private readonly HttpClient _httpClient;

        public InstanceManagerTests()
        {
            _httpClient = new HttpClient();

            _loggerProvider = new TestLoggerProvider();
            var loggerProviderFactory = new TestLoggerProviderFactory(_loggerProvider);
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            _settingsManager = new ScriptSettingsManager();
            _instanceManager = new InstanceManager(_settingsManager, null, loggerFactory, _httpClient);
        }

        [Fact]
        public async Task StartAssignment_AppliesAssignmentContext()
        {
            var envValue = new
            {
                Name = Path.GetTempFileName().Replace(".", string.Empty),
                Value = Guid.NewGuid().ToString()
            };

            _settingsManager.SetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            WebScriptHostManager.ResetStandbyMode();
            var context = new HostAssignmentContext
            {
                Environment = new Dictionary<string, string>
                {
                    { envValue.Name, envValue.Value }
                }
            };
            bool result = _instanceManager.StartAssignment(context);
            Assert.True(result);
            Assert.True(WebScriptHostManager.InStandbyMode);

            // specialization is done in the background
            await Task.Delay(500);

            var value = Environment.GetEnvironmentVariable(envValue.Name);
            Assert.Equal(value, envValue.Value);

            // verify logs
            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Starting Assignment", p),
                p => Assert.StartsWith("Applying 1 app setting(s)", p),
                p => Assert.StartsWith("Triggering specialization", p));

            // calling again should return false, since we're no longer
            // in placeholder mode
            _loggerProvider.ClearAllLogMessages();
            result = _instanceManager.StartAssignment(context);
            Assert.False(result);

            logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Assign called while host is not in placeholder mode", p));
        }

        [Fact]
        public void StartAssignment_ReturnsFalse_WhenNotInStandbyMode()
        {
            Assert.False(WebScriptHostManager.InStandbyMode);

            var context = new HostAssignmentContext();
            bool result = _instanceManager.StartAssignment(context);
            Assert.False(result);
        }

        [Fact]
        public async Task ValidateContext_InvalidZipUrl_ReturnsError()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, "http://invalid.com/invalid/dne" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment
            };

            string error = await _instanceManager.ValidateContext(assignmentContext);
            Assert.Equal("Invalid zip url specified (StatusCode: NotFound)", error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Validating host assignment context (SiteId: 1234, SiteName: 'TestSite')", p),
                p => Assert.StartsWith("Invalid zip url specified (StatusCode: NotFound)", p));
        }

        [Fact]
        public async Task ValidateContext_Succeeds()
        {
            var environment = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsiteZipDeployment, "http://microsoft.com" }
            };
            var assignmentContext = new HostAssignmentContext
            {
                SiteId = 1234,
                SiteName = "TestSite",
                Environment = environment
            };

            string error = await _instanceManager.ValidateContext(assignmentContext);
            Assert.Null(error);

            var logs = _loggerProvider.GetAllLogMessages().Select(p => p.FormattedMessage).ToArray();
            Assert.Collection(logs,
                p => Assert.StartsWith("Validating host assignment context (SiteId: 1234, SiteName: 'TestSite')", p));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, null);
        }
    }
}
