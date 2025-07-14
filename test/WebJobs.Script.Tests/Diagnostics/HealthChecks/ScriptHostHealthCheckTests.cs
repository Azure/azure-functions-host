// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class ScriptHostHealthCheckTests
    {
        [Theory]
        [InlineData(ScriptHostState.Default, "No script host available", HealthStatus.Unhealthy)]
        [InlineData(ScriptHostState.Starting, "Script host not started", HealthStatus.Unhealthy)]
        [InlineData(ScriptHostState.Initialized, null, HealthStatus.Healthy)]
        [InlineData(ScriptHostState.Running, null, HealthStatus.Healthy)]
        [InlineData(ScriptHostState.Stopping, "Script host stopping", HealthStatus.Unhealthy)]
        [InlineData(ScriptHostState.Stopped, "Script host stopped", HealthStatus.Unhealthy)]
        [InlineData(ScriptHostState.Offline, "Script host offline", HealthStatus.Unhealthy)]
        [InlineData((ScriptHostState)100, "Script host in unknown state", HealthStatus.Unhealthy)]
        public async Task CheckHealthAsync_MatchesScriptHostHealth(
            ScriptHostState state, string description, HealthStatus status)
        {
            // arrange
            Mock<IScriptHostManager> manager = new();
            manager.Setup(m => m.State).Returns(state);
            ScriptHostHealthCheck healthCheck = new(manager.Object);

            // act
            HealthCheckResult result = await healthCheck.CheckHealthAsync(new(), default);

            // assert
            result.Status.Should().Be(status);
            result.Description.Should().Be(description);
            result.Exception.Should().BeNull();
        }

        [Fact]
        public async Task CheckHealthAsync_Error_IncludesException()
        {
            // arrange
            Exception error = new("Some exception message");
            Mock<IScriptHostManager> manager = new();
            manager.Setup(m => m.State).Returns(ScriptHostState.Error);
            manager.Setup(m => m.LastError).Returns(error);
            ScriptHostHealthCheck healthCheck = new(manager.Object);

            // act
            HealthCheckResult result = await healthCheck.CheckHealthAsync(new(), default);

            // assert
            result.Status.Should().Be(HealthStatus.Unhealthy);
            result.Description.Should().Be($"Script host in error state: {Environment.NewLine}{error.Message}");
            result.Exception.Should().Be(error);
        }
    }
}
