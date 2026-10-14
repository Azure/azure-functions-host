// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class MappedHttpEndpointTests
{
    [Fact]
    public void AssignedOrigin_OverridesConfiguredOriginAndPreservesLocalDestination()
    {
        WorkerPodStateManager manager = CreateManager();
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest("http://allocated.example:49187/"), manager));
        WorkerProxyOptions options = new() { HttpProxyEndpoint = "http://legacy.example:28080/" };
        WorkerHttpCapabilityProvider provider = new(Options.Create(options), NullLogger<WorkerHttpCapabilityProvider>.Instance, manager);
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/worker/" };

        Assert.Equal(new Uri("http://localhost:1234/worker/"), provider.FinalizeCapabilities(capabilities));
        Assert.Equal("http://allocated.example:49187/", capabilities["HttpUri"]);
        Assert.Equal(new Uri("http://allocated.example:49187/"), manager.HttpProxyEndpoint);
    }

    [Fact]
    public void AssignmentRetry_IdenticalOriginSucceedsAndChangedOriginConflicts()
    {
        WorkerPodStateManager manager = CreateManager();
        WorkerAssignRequest request = CreateRequest("http://allocated.example:49187/");
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(request, manager));
        WorkerPodState assigned = manager.State;

        Assert.IsType<NoContent>(ManagementApiHandlers.AssignWorker(CreateRequest("http://allocated.example:49187/"), manager));
        Assert.Same(assigned, manager.State);
        JsonHttpResult<WorkerApiErrorResponse> conflict = Assert.IsType<JsonHttpResult<WorkerApiErrorResponse>>(
            ManagementApiHandlers.AssignWorker(CreateRequest("http://allocated.example:49191/"), manager));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Same(assigned, manager.State);
        Assert.Equal(new Uri("http://allocated.example:49187/"), manager.HttpProxyEndpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative")]
    [InlineData("ftp://allocated.example:1234/")]
    [InlineData("http://allocated.example:0/")]
    [InlineData("http://0.0.0.0:1234/")]
    [InlineData("http://[::]:1234/")]
    [InlineData("http://*:1234/")]
    [InlineData("http://+:1234/")]
    [InlineData("http://{{status.podIP}}:1234/")]
    [InlineData("http://user:password@allocated.example:1234/")]
    [InlineData("http://allocated.example:1234/prefix/")]
    [InlineData("http://allocated.example:1234/?query=value")]
    [InlineData("http://allocated.example:1234/#fragment")]
    public void MalformedOrigin_DoesNotClaimAssignment(string endpoint)
    {
        WorkerPodStateManager manager = CreateManager();
        WorkerPodState initial = manager.State;
        JsonHttpResult<RequestValidationResponse> result = Assert.IsType<JsonHttpResult<RequestValidationResponse>>(
            ManagementApiHandlers.AssignWorker(CreateRequest(endpoint), manager));
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(new("InvalidValue", "httpProxyEndpoint"), Assert.Single(result.Value!.Errors));
        Assert.Same(initial, manager.State);
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest("http://allocated.example:49187/"), manager));
    }

    [Fact]
    public void MissingAssignedOrigin_HttpFailsButNonHttpRemainsAvailable()
    {
        WorkerPodStateManager manager = CreateManager();
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest(null), manager));
        WorkerHttpCapabilityProvider provider = new(Options.Create(new WorkerProxyOptions()), NullLogger<WorkerHttpCapabilityProvider>.Instance, manager);
        Dictionary<string, string> capabilities = new() { ["WorkerIndexing"] = "true" };
        Assert.Null(provider.FinalizeCapabilities(capabilities));
        Assert.Equal("true", capabilities["WorkerIndexing"]);

        capabilities["HttpUri"] = "http://localhost:1234/";
        Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities));
        Assert.Equal("http://localhost:1234/", capabilities["HttpUri"]);
    }

    [Fact]
    public void LocalDestinationEqualToProxyOrigin_FailsWithoutAdvertisingLoop()
    {
        WorkerPodStateManager manager = CreateManager();
        Assert.IsType<Created>(ManagementApiHandlers.AssignWorker(CreateRequest("http://localhost:1234/"), manager));
        WorkerHttpCapabilityProvider provider = new(Options.Create(new WorkerProxyOptions()), NullLogger<WorkerHttpCapabilityProvider>.Instance, manager);
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/worker/" };

        Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities));
        Assert.Equal("http://localhost:1234/worker/", capabilities["HttpUri"]);
    }

    private static WorkerPodStateManager CreateManager()
    {
        WorkerPodStateManager manager = new(Options.Create(new WorkerProxyOptions { PodName = "mapped-pod" }), TimeProvider.System);
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");
        return manager;
    }

    private static WorkerAssignRequest CreateRequest(string? endpoint)
        => new()
        {
            StartupMode = nameof(WorkerStartupMode.Preconfigured),
            FunctionAppName = "app",
            FunctionGroupName = "http",
            IsAlwaysReady = false,
            Environment = [],
            FunctionAppDirectory = string.Empty,
            HttpProxyEndpoint = endpoint
        };
}
