// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.WorkerProxy.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class MappedHttpEndpointTests
{
    [Fact]
    public void StartupOrigin_IsAdvertisedAndPreservesLocalDestination()
    {
        // This is the normalized key produced by the environment provider for
        // WORKERPROXY__HTTPPROXYENDPOINT, without mutating the process environment.
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["WORKERPROXY:HTTPPROXYENDPOINT"] = "http://allocated.example:49187/" }).Build();
        WorkerProxyOptions options = new();
        configuration.GetSection(WorkerProxyOptions.SectionName).Bind(options);
        WorkerHttpCapabilityProvider provider = CreateProvider(options);
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/worker/" };

        Assert.Equal(new Uri("http://localhost:1234/worker/"), provider.FinalizeCapabilities(capabilities));
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
    public void MalformedStartupOrigin_IsNeverAdvertised(string endpoint)
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { HttpProxyEndpoint = endpoint });
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/" };
        Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities));
        Assert.Equal("http://localhost:1234/", capabilities["HttpUri"]);
    }

    [Fact]
    public void MissingStartupOrigin_HttpFailsButNonHttpRemainsAvailable()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new());
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
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { HttpProxyEndpoint = "http://localhost:1234/" });
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/worker/" };
        Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities));
        Assert.Equal("http://localhost:1234/worker/", capabilities["HttpUri"]);
    }

    private static WorkerHttpCapabilityProvider CreateProvider(WorkerProxyOptions options)
        => new(Options.Create(options), NullLogger<WorkerHttpCapabilityProvider>.Instance);
}
