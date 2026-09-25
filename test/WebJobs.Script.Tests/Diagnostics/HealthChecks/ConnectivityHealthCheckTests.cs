// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics.HealthChecks
{
    // Demonstrates the extension-owned connectivity abstraction end-to-end (Network Troubleshooter):
    // an extension registers a plain IConnectivityValidator (no HealthChecks dependency); the host
    // adapter enumerates the app's triggers, matches the validator by trigger type, and hands it the
    // binding's connection + settings — turning it into a connectivity health check result.
    public class ConnectivityHealthCheckTests
    {
        [Fact]
        public async Task CheckHealthAsync_InvokesMatchingValidator_WithBindingConnectionAndSettings()
        {
            // Arrange: an app with one Event Hub trigger.
            BindingMetadata trigger = new()
            {
                Name = "events",
                Type = "eventHubTrigger",
                Connection = "MyEventHubConnection",
                Direction = BindingDirection.In,
            };
            trigger.Properties["eventHubName"] = "my-hub";
            FunctionMetadata function = new() { Name = "ProcessEvents" };
            function.Bindings.Add(trigger);

            Mock<IFunctionMetadataManager> metadataManager = new();
            metadataManager
                .Setup(m => m.GetFunctionMetadata(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(ImmutableArray.Create(function));

            RecordingValidator validator = new("eventHubTrigger");
            ConnectivityHealthCheck check = new(new[] { validator }, metadataManager.Object);

            // Act
            HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

            // Assert: the adapter found the Event Hub trigger, matched the validator by trigger type,
            // and handed it the binding's connection + settings.
            result.Status.Should().Be(HealthStatus.Healthy);
            validator.Invoked.Should().BeTrue();
            validator.LastContext.Connection.Should().Be("MyEventHubConnection");
            validator.LastContext.Properties["eventHubName"].Should().Be("my-hub");
        }

        [Fact]
        public async Task CheckHealthAsync_UnhealthyValidator_ReportsUnhealthy()
        {
            BindingMetadata trigger = new()
            {
                Name = "events",
                Type = "eventHubTrigger",
                Connection = "MyEventHubConnection",
                Direction = BindingDirection.In,
            };
            FunctionMetadata function = new() { Name = "ProcessEvents" };
            function.Bindings.Add(trigger);

            Mock<IFunctionMetadataManager> metadataManager = new();
            metadataManager
                .Setup(m => m.GetFunctionMetadata(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(ImmutableArray.Create(function));

            RecordingValidator validator = new("eventHubTrigger")
            {
                Result = ConnectivityResult.Unhealthy("Auth failed"),
            };
            ConnectivityHealthCheck check = new(new[] { validator }, metadataManager.Object);

            HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

            result.Status.Should().Be(HealthStatus.Unhealthy);
        }

        [Fact]
        public async Task CheckHealthAsync_NoValidatorForTrigger_SkipsAndReportsHealthy()
        {
            BindingMetadata trigger = new()
            {
                Name = "events",
                Type = "serviceBusTrigger",
                Connection = "MyServiceBusConnection",
                Direction = BindingDirection.In,
            };
            FunctionMetadata function = new() { Name = "ProcessMessages" };
            function.Bindings.Add(trigger);

            Mock<IFunctionMetadataManager> metadataManager = new();
            metadataManager
                .Setup(m => m.GetFunctionMetadata(It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(ImmutableArray.Create(function));

            // Only an Event Hubs validator is registered; the Service Bus trigger has no validator yet.
            RecordingValidator validator = new("eventHubTrigger");
            ConnectivityHealthCheck check = new(new[] { validator }, metadataManager.Object);

            HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

            validator.Invoked.Should().BeFalse();
            result.Status.Should().Be(HealthStatus.Healthy);
        }

        private sealed class RecordingValidator : IConnectivityValidator
        {
            public RecordingValidator(string triggerType) => TriggerType = triggerType;

            public string TriggerType { get; }

            public bool Invoked { get; private set; }

            public ConnectivityContext LastContext { get; private set; }

            public ConnectivityResult Result { get; set; } = ConnectivityResult.Healthy();

            public Task<ConnectivityResult> ValidateAsync(ConnectivityContext context, CancellationToken cancellationToken)
            {
                Invoked = true;
                LastContext = context;
                return Task.FromResult(Result);
            }
        }
    }
}
