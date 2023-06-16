// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class HostWarmupMiddlewareTests
    {
        [Fact]
        public void IsWarmUpRequest_ReturnsExpectedValue()
        {
            var environment = new TestEnvironment();
            var hostEnvironment = new ScriptWebHostEnvironment(environment);
            var request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");

            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            // Reset environment
            environment.Clear();
            hostEnvironment = new ScriptWebHostEnvironment(environment);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "12345");
            Assert.True(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/csharphttpwarmup");
            Assert.True(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz123");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/foo");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            // Reset environment
            environment.Clear();
            hostEnvironment = new ScriptWebHostEnvironment(environment);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
            Assert.True(environment.IsAnyLinuxConsumption());
            Assert.True(environment.IsLinuxConsumptionOnAtlas());
            Assert.False(environment.IsFlexConsumptionSku());
            Assert.True(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            // Reset environment
            environment.Clear();
            hostEnvironment = new ScriptWebHostEnvironment(environment);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.False(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.LegionServiceHost, "1");
            Assert.True(environment.IsAnyLinuxConsumption());
            Assert.False(environment.IsLinuxConsumptionOnAtlas());
            Assert.True(environment.IsFlexConsumptionSku());
            Assert.True(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));
        }

        [Fact]
        public void ReadRuntimeAssemblyFiles_VerifyLogs()
        {
            var environment = new TestEnvironment();
            var hostEnvironment = new ScriptWebHostEnvironment(environment);
            var testLoggerFactory = new LoggerFactory();
            TestLoggerProvider testLoggerProvider = new TestLoggerProvider();
            testLoggerFactory.AddProvider(testLoggerProvider);
            ILogger<HostWarmupMiddleware> testLogger = testLoggerFactory.CreateLogger<HostWarmupMiddleware>();
            HostWarmupMiddleware hostWarmupMiddleware = new HostWarmupMiddleware(null, new Mock<IScriptWebHostEnvironment>().Object, environment, new Mock<IScriptHostManager>().Object, testLogger);
            hostWarmupMiddleware.ReadRuntimeAssemblyFiles();
            // Assert
            var traces = testLoggerProvider.GetAllLogMessages();
            Assert.True(traces.Any(m => m.FormattedMessage.Contains("Number of files read:")));
        }
    }
}
