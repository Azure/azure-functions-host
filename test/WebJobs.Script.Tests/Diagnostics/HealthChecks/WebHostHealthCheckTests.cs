// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    public class WebHostHealthCheckTests
    {
        public enum LifetimeState
        {
            NotStarted = 0,
            Running = 1,
            Stopping = 2,
            Stopped = 3,
        }

        [Theory]
        [InlineData(LifetimeState.NotStarted)]
        [InlineData(LifetimeState.Running)]
        [InlineData(LifetimeState.Stopping)]
        [InlineData(LifetimeState.Stopped)]
        public async Task CheckHealthAsync_MatchesApplicationHealth(LifetimeState state)
        {
            // arrange
            IHostApplicationLifetime lifetime = CreateLifetime(state, out string description, out HealthStatus expected);
            WebHostHealthCheck healthCheck = new(lifetime);

            // act
            HealthCheckResult result = await healthCheck.CheckHealthAsync(new(), default);

            // assert
            result.Status.Should().Be(expected);
            result.Exception.Should().BeNull();
            result.Description.Should().Be(description);
        }

        private static IHostApplicationLifetime CreateLifetime(
            LifetimeState state, out string description, out HealthStatus status)
        {
            Mock<IHostApplicationLifetime> lifetime = new();

            int lifecycle = (int)state;

            description = "Not Started";
            status = HealthStatus.Unhealthy;

            if (lifecycle > 0)
            {
                description = null;
                lifetime.Setup(m => m.ApplicationStarted).Returns(new CancellationToken(true));
                status = HealthStatus.Healthy;
            }

            if (lifecycle > 1)
            {
                description = "Stopping";
                lifetime.Setup(m => m.ApplicationStopping).Returns(new CancellationToken(true));
                status = HealthStatus.Unhealthy;
            }

            if (lifecycle > 2)
            {
                description = "Stopped";
                lifetime.Setup(m => m.ApplicationStopped).Returns(new CancellationToken(true));
                status = HealthStatus.Unhealthy;
            }

            return lifetime.Object;
        }
    }
}
