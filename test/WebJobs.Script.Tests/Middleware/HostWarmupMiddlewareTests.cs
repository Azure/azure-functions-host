// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
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
            Assert.True(environment.IsLinuxConsumption());
            Assert.True(HostWarmupMiddleware.IsWarmUpRequest(request, hostEnvironment.InStandbyMode, environment));
        }
    }
}
