// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class MappedHttpEndpointTests
{
    [Fact]
    public void AssignmentOrigin_IsAdvertisedAndPreservesLocalDestination()
    {
        // An assignment must supersede even explicit legacy configuration.
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { HttpProxyEndpoint = "http://legacy.example:28080/" });
        WorkerAssignment assignment = CreateAssignment("http://allocated.example:49187/");
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/worker/" };

        Assert.Equal(new Uri("http://localhost:1234/worker/"), provider.FinalizeCapabilities(capabilities, assignment));
        Assert.Equal("http://allocated.example:49187/", capabilities["HttpUri"]);
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
    public void MalformedAssignmentOrigin_IsRejected(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => CreateAssignment(endpoint));
    }

    [Fact]
    public void MissingAssignmentOrigin_HttpFailsButNonHttpRemainsAvailable()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { HttpProxyEndpoint = "http://legacy.example:28080/" });
        WorkerAssignment assignment = CreateAssignment(null);
        Dictionary<string, string> capabilities = new() { ["WorkerIndexing"] = "true" };
        Assert.Null(provider.FinalizeCapabilities(capabilities, assignment));
        Assert.Equal("true", capabilities["WorkerIndexing"]);
        capabilities["HttpUri"] = "http://localhost:1234/";
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities, assignment));
        Assert.Contains("accepted assignment", exception.Message);
        Assert.Equal("http://localhost:1234/", capabilities["HttpUri"]);
    }

    [Fact]
    public void NonHttpWorker_DoesNotAdvertiseAssignedOrigin()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new());
        WorkerAssignment assignment = CreateAssignment("http://allocated.example:49187/");
        Dictionary<string, string> capabilities = new() { ["WorkerIndexing"] = "true" };

        Assert.Null(provider.FinalizeCapabilities(capabilities, assignment));
        Assert.False(capabilities.ContainsKey("HttpUri"));
        Assert.Equal("true", capabilities["WorkerIndexing"]);
    }

    [Fact]
    public void LocalDestinationEqualToProxyOrigin_FailsWithoutAdvertisingLoop()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new());
        WorkerAssignment assignment = CreateAssignment("http://localhost:1234/");
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/worker/" };
        Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities, assignment));
        Assert.Equal("http://localhost:1234/worker/", capabilities["HttpUri"]);
    }

    private static WorkerHttpCapabilityProvider CreateProvider(WorkerProxyOptions options)
        => new(Options.Create(options), NullLogger<WorkerHttpCapabilityProvider>.Instance);

    private static WorkerAssignment CreateAssignment(string? endpoint)
        => new(WorkerStartupMode.Preconfigured, "app", "http", false, new Dictionary<string, string>(), string.Empty, endpoint);
}
