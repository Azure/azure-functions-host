// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.StandbyModeTestsWindows)]
    public class StandbyManagerE2ETests_Windows : StandbyManagerE2ETestBase
    {
        private static IDictionary<string, string> _settings;
        public StandbyManagerE2ETests_Windows()
        {
            _settings = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1" },
                { EnvironmentSettingNames.AzureWebsiteContainerReady, null },
                { EnvironmentSettingNames.AzureWebsiteSku, "Dynamic" },
                { EnvironmentSettingNames.AzureWebsiteHomePath, null },
                { "AzureWebEncryptionKey", "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248" },
             };
        }

        [Fact]
        public async Task StandbyModeE2E_Dotnet()
        {
            _settings.Add(EnvironmentSettingNames.AzureWebsiteInstanceId, Guid.NewGuid().ToString());
            var environment = new TestEnvironment(_settings);
            await InitializeTestHostAsync("Windows", environment);

            await VerifyWarmupSucceeds();
            await VerifyWarmupSucceeds(restart: true);

            // now specialize the host
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, "dotnet");

            Assert.False(environment.IsPlaceholderModeEnabled());
            Assert.True(environment.IsContainerReady());

            // give time for the specialization to happen
            string[] logLines = null;
            await TestHelpers.Await(() =>
            {
                // wait for the trace indicating that the host has been specialized
                logLines = _loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
                return logLines.Contains("Generating 0 job function(s)") && logLines.Contains("Stopping JobHost");
            }, userMessageCallback: () => string.Join(Environment.NewLine, _loggerProvider.GetAllLogMessages().Select(p => $"[{p.Timestamp.ToString("HH:mm:ss.fff")}] {p.FormattedMessage}")));

            // verify the rest of the expected logs
            logLines = _loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
            Assert.True(logLines.Count(p => p.Contains("Stopping JobHost")) >= 1);
            Assert.Equal(1, logLines.Count(p => p.Contains("Creating StandbyMode placeholder function directory")));
            Assert.Equal(1, logLines.Count(p => p.Contains("StandbyMode placeholder function directory created")));
            Assert.Equal(2, logLines.Count(p => p.Contains("Host is in standby mode")));
            Assert.Equal(2, logLines.Count(p => p.Contains("Executed 'Functions.WarmUp' (Succeeded")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Starting host specialization")));
            Assert.Equal(1, logLines.Count(p => p.Contains("Starting language worker channel specialization")));
            Assert.Equal(3, logLines.Count(p => p.Contains($"Starting Host (HostId={_expectedHostId}")));
            Assert.Equal(3, logLines.Count(p => p.Contains($"Loading functions metadata")));
            Assert.Equal(2, logLines.Count(p => p.Contains($"1 functions loaded")));
            Assert.Equal(1, logLines.Count(p => p.Contains($"0 functions loaded")));
            Assert.Equal(3, logLines.Count(p => p.Contains($"Loading proxies metadata")));
            Assert.Equal(3, logLines.Count(p => p.Contains("Initializing Azure Function proxies")));
            Assert.Equal(2, logLines.Count(p => p.Contains($"1 proxies loaded")));
            Assert.Equal(1, logLines.Count(p => p.Contains($"0 proxies loaded")));
            Assert.Contains("Generating 0 job function(s)", logLines);

            // Verify that the internal cache has reset
            Assert.NotSame(GetCachedTimeZoneInfo(), _originalTimeZoneInfoCache);
        }

        [Fact(Skip = "https://github.com/Azure/azure-functions-host/issues/4230")]
        public async Task StandbyModeE2E_Java()
        {
            _settings.Add(EnvironmentSettingNames.AzureWebsiteInstanceId, Guid.NewGuid().ToString());
            await Verify_StandbyModeE2E_Java();
        }

        [Fact(Skip = "https://github.com/Azure/azure-functions-host/issues/4230")]
        public async Task StandbyModeE2E_JavaTemplateSite()
        {
            _settings.Add(EnvironmentSettingNames.AzureWebsiteInstanceId, Guid.NewGuid().ToString());
            _settings.Add(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, LanguageWorkerConstants.JavaLanguageWorkerName);
            await Verify_StandbyModeE2E_Java();
        }

        private async Task Verify_StandbyModeE2E_Java()
        {
            var environment = new TestEnvironment(_settings);
            await InitializeTestHostAsync("Windows_Java", environment);

            await VerifyWarmupSucceeds();
            await VerifyWarmupSucceeds(restart: true);

            // Get java process Id before specialization
            IEnumerable<int> javaProcessesBefore = Process.GetProcessesByName("java").Select(p => p.Id);

            // now specialize the host
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "1");
            environment.SetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName, "java");

            Assert.False(environment.IsPlaceholderModeEnabled());
            Assert.True(environment.IsContainerReady());

            // give time for the specialization to happen
            string[] logLines = null;
            await TestHelpers.Await(() =>
            {
                // wait for the trace indicating that the host has been specialized
                logLines = _loggerProvider.GetAllLogMessages().Where(p => p.FormattedMessage != null).Select(p => p.FormattedMessage).ToArray();
                return logLines.Contains("Generating 0 job function(s)") && logLines.Contains("Stopping JobHost");
            }, userMessageCallback: () => string.Join(Environment.NewLine, _loggerProvider.GetAllLogMessages().Select(p => $"[{p.Timestamp.ToString("HH:mm:ss.fff")}] {p.FormattedMessage}")));

            IEnumerable<int> javaProcessesAfter = Process.GetProcessesByName("java").Select(p => p.Id);

            // Verify number of java processes before and after specialization are the same.
            Assert.Equal(javaProcessesBefore.Count(), javaProcessesAfter.Count());

            //Verify atleast one java process is running
            Assert.True(javaProcessesAfter.Count() >= 1);

            // Verify Java same java process is used after host restart
            var result = javaProcessesBefore.Where(pId1 => !javaProcessesAfter.Any(pId2 => pId2 == pId1));
            Assert.Equal(0, result.Count());
        }
    }
}
