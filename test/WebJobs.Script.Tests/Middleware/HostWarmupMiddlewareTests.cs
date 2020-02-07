// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Microsoft.AspNetCore.Http;
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
            RequestDelegate next = (HttpContext c) => Task.CompletedTask;
            var hostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var loggerMock = new Mock<ILogger<HostWarmupMiddleware>>(MockBehavior.Strict);
            var middleware = new HostWarmupMiddleware(next, hostEnvironment, environment, hostManagerMock.Object, loggerMock.Object);

            Assert.False(middleware.IsWarmUpRequest(request, hostEnvironment, environment));

            // Reset environment
            environment.Clear();
            hostEnvironment = new ScriptWebHostEnvironment(environment);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.False(middleware.IsWarmUpRequest(request, hostEnvironment, environment));

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "12345");
            Assert.True(middleware.IsWarmUpRequest(request, hostEnvironment, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/csharphttpwarmup");
            Assert.True(middleware.IsWarmUpRequest(request, hostEnvironment, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, "xyz123");
            Assert.False(middleware.IsWarmUpRequest(request, hostEnvironment, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/foo");
            Assert.False(middleware.IsWarmUpRequest(request, hostEnvironment, environment));

            // Reset environment
            environment.Clear();
            hostEnvironment = new ScriptWebHostEnvironment(environment);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.False(middleware.IsWarmUpRequest(request, hostEnvironment, environment));

            request = HttpTestHelpers.CreateHttpRequest("POST", "http://azure.com/api/warmup");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
            Assert.True(environment.IsLinuxConsumption());
            Assert.True(middleware.IsWarmUpRequest(request, hostEnvironment, environment));
        }
    }
}
