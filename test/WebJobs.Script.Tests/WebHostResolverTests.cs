// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

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
                var resolver = new WebHostResolver(settingsManager, secretManagerFactoryMock.Object, eventManagerMock.Object);

                var settings = new WebHostSettings
                {
                    ScriptPath = @"c:\some\path",
                    LogPath = @"c:\log\path",
                    SecretsPath = @"c:\secrets\path"
                };

                ScriptHostConfiguration configuration = resolver.GetScriptHostConfiguration(settings);

                Assert.Equal("testsite", configuration.HostConfig.HostId);
            }
        }

        [Fact]
        public void CreateScriptHostConfiguration_StandbyMode_ReturnsExpectedConfiguration()
        {
            var settings = new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Path.GetTempPath(), "Functions", "Standby")
            };

            var config = WebHostResolver.CreateScriptHostConfiguration(settings, true);

            Assert.Equal(FileLoggingMode.DebugOnly, config.FileLoggingMode);
            Assert.Null(config.HostConfig.StorageConnectionString);
            Assert.Null(config.HostConfig.DashboardConnectionString);
        }

        [Fact]
        public async Task GetTraceWriter_GetLogger_ReturnsExpectedValue()
        {
            var settingsManager = new ScriptSettingsManager();
            var secretManagerFactoryMock = new Mock<ISecretManagerFactory>();
            var eventManagerMock = new Mock<IScriptEventManager>();
            var resolver = new WebHostResolver(settingsManager, secretManagerFactoryMock.Object, eventManagerMock.Object);

            using (resolver)
            {
                string tempRoot = Path.GetTempPath();
                var settings = new WebHostSettings
                {
                    LogPath = Path.Combine(tempRoot, @"Functions"),
                    ScriptPath = Path.Combine(tempRoot, @"Functions"),
                    SecretsPath = Path.Combine(tempRoot, @"Functions"),
                };
                File.WriteAllText(Path.Combine(settings.ScriptPath, "host.json"), "{ id: 'testid' }");

                var secretsManagerMock = new Mock<ISecretManager>();
                secretsManagerMock.Setup(p => p.PurgeOldSecretsAsync(settings.SecretsPath, It.IsAny<TraceWriter>(), It.IsAny<ILogger>())).Returns(Task.CompletedTask);
                secretManagerFactoryMock.Setup(p => p.Create(settingsManager, It.IsAny<TraceWriter>(), It.IsAny<ILoggerFactory>(), It.IsAny<ISecretsRepository>())).Returns(secretsManagerMock.Object);

                // ensure that the returned trace writer isn't null even though the
                // host hasn't been initialized yet
                var traceWriter = resolver.GetTraceWriter(settings);
                Assert.NotNull(traceWriter);
                var hostManager = resolver.GetWebScriptHostManager(settings);
                Assert.Null(hostManager.Instance?.TraceWriter);

                // ensure that the returned logger factory isn't null even though the host
                // hasn't been initialized yet
                var loggerFactory = resolver.GetLoggerFactory(settings);
                Assert.NotNull(loggerFactory);

                // now wait for the host to be fully initialized
                Task ignored = Task.Run(() => hostManager.RunAndBlock());
                await TestHelpers.Await(() => hostManager.State == ScriptHostState.Error || hostManager.CanInvoke());

                if (hostManager.State == ScriptHostState.Error)
                {
                    Assert.True(false, hostManager.LastError.Message);
                }

                // verify the trace writer returned is the host trace writer
                traceWriter = resolver.GetTraceWriter(settings);
                Assert.Same(hostManager.Instance.TraceWriter, traceWriter);

                // verify the logger factory returned is the host trace writer
                var config = resolver.GetScriptHostConfiguration(settings);
                loggerFactory = resolver.GetLoggerFactory(settings);
                Assert.Same(loggerFactory, config.HostConfig.LoggerFactory);
            }
        }
    }
}
