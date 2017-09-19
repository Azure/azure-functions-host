// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;
using Xunit;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebHostResolverTests
    {
        [Fact]
        public void GetScriptHostConfiguration_SetsHostId()
        {
            var environmentSettings = new Dictionary<string, string>
            {
                { EnvironmentSettingNames.AzureWebsiteName, "testsite" },
                { EnvironmentSettingNames.AzureWebsiteSlotName, "production" },
            };

            using (var environment = new TestScopedEnvironmentVariable(environmentSettings))
            {
                var settingsManager = new ScriptSettingsManager();
                var secretManagerFactoryMock = new Mock<ISecretManagerFactory>();
                var eventManagerMock = new Mock<IScriptEventManager>();
                var routerMock = new Mock<IWebJobsRouter>();
                var settings = new WebHostSettings
                {
                    ScriptPath = @"c:\some\path",
                    LogPath = @"c:\log\path",
                    SecretsPath = @"c:\secrets\path"
                };

                var resolver = new WebHostResolver(settingsManager, secretManagerFactoryMock.Object, eventManagerMock.Object, settings, routerMock.Object, new DefaultLoggerFactoryBuilder());

                ScriptHostConfiguration configuration = resolver.GetScriptHostConfiguration(settings);

                Assert.Equal("testsite", configuration.HostConfig.HostId);
            }
        }

        [Fact]
        public void CreateScriptHostConfiguration_StandbyMode_ReturnsExpectedConfiguration()
        {
            var settings = new WebHostSettings
            {
                IsSelfHost = true
            };

            var config = WebHostResolver.CreateScriptHostConfiguration(settings, true);

            Assert.Equal(FileLoggingMode.DebugOnly, config.FileLoggingMode);
            Assert.Null(config.HostConfig.StorageConnectionString);
            Assert.Null(config.HostConfig.DashboardConnectionString);
            Assert.Equal(Path.Combine(Path.GetTempPath(), "Functions", "Standby"), config.RootScriptPath);
        }
    }
}
