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
    public static TheoryData<string> InvalidAssignedEndpoints => new()
    {
        string.Empty, " ", "relative", ":1234", "mapped.example:",
        "mapped.example:0", "mapped.example:65536", "mapped.example:-1", "mapped.example:+1234", "mapped.example:port",
        "0.0.0.0:1234", "[::]:1234", "::1:1234", "*:1234", "+:1234", "{{status.podIP}}:1234",
        "user:password@mapped.example:1234", "mapped.example:1234/", "mapped.example:1234/prefix/",
        "mapped.example:1234?query=value", "mapped.example:1234#fragment", "//mapped.example:1234",
        "mapped.example\\path:1234", "mapped%2eexample:1234", "mapped.example :1234", " mapped.example:1234", "mapped.example:1234 ",
        "http://mapped.example:1234", "https://mapped.example:1234", "ftp://mapped.example:1234",
    };

    [Theory]
    [InlineData("100.86.0.4:49123", "http://100.86.0.4:49123/")]
    [InlineData("allocated.example:49187", "http://allocated.example:49187/")]
    [InlineData("allocated.example:80", "http://allocated.example/")]
    [InlineData("allocated.example:443", "http://allocated.example:443/")]
    [InlineData("[::1]:49123", "http://[::1]:49123/")]
    public void AssignmentEndpoint_UsesListenerSchemeAndPreservesLocalDestination(string endpoint, string origin)
    {
        // An assignment must supersede even explicit legacy configuration.
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { HttpProxyEndpoint = "https://legacy.example:28080/" });
        WorkerAssignment assignment = CreateAssignment(endpoint);
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/worker/" };

        Assert.Equal(new Uri("http://localhost:1234/worker/"), provider.FinalizeCapabilities(capabilities, assignment));
        Assert.Equal(origin, capabilities["HttpUri"]);
        Assert.Equal(endpoint, assignment.HttpProxyEndpoint);
    }

    [Theory]
    [MemberData(nameof(InvalidAssignedEndpoints))]
    public void MalformedAssignmentEndpoint_IsRejected(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => CreateAssignment(endpoint));
    }

    [Theory]
    [InlineData("http", "100.86.0.4:49123", "http://100.86.0.4:49123/")]
    [InlineData("https", "100.86.0.4:49123", "https://100.86.0.4:49123/")]
    [InlineData("https", "allocated.example:80", "https://allocated.example:80/")]
    [InlineData("https", "allocated.example:443", "https://allocated.example/")]
    [InlineData("https", "[::1]:49123", "https://[::1]:49123/")]
    [InlineData("http", "allocated.example:1", "http://allocated.example:1/")]
    [InlineData("https", "allocated.example:65535", "https://allocated.example:65535/")]
    public void AssignedEndpoint_UsesProxySelectedSchemeAndPreservesMappedPort(string scheme, string endpoint, string origin)
    {
        Uri actual = Assert.IsType<Uri>(WorkerProxyOptionsValidator.ResolveHttpProxyOrigin(endpoint, scheme));
        Assert.Equal(origin, actual.AbsoluteUri);
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
        WorkerAssignment assignment = CreateAssignment("allocated.example:49187");
        Dictionary<string, string> capabilities = new() { ["WorkerIndexing"] = "true" };

        Assert.Null(provider.FinalizeCapabilities(capabilities, assignment));
        Assert.False(capabilities.ContainsKey("HttpUri"));
        Assert.Equal("true", capabilities["WorkerIndexing"]);
    }

    [Fact]
    public void LocalDestinationEqualToProxyOrigin_FailsWithoutAdvertisingLoop()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new());
        WorkerAssignment assignment = CreateAssignment("localhost:1234");
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/worker/" };
        Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities, assignment));
        Assert.Equal("http://localhost:1234/worker/", capabilities["HttpUri"]);
    }

    private static WorkerHttpCapabilityProvider CreateProvider(WorkerProxyOptions options)
    {
        IOptions<WorkerProxyOptions> configuredOptions = Options.Create(options);
        WorkerProxyEndpointConfiguration endpoints = new(configuredOptions);
        endpoints.Configure(new());
        return new(configuredOptions, NullLogger<WorkerHttpCapabilityProvider>.Instance, endpoints);
    }

    private static WorkerAssignment CreateAssignment(string? endpoint)
        => new(WorkerStartupMode.Preconfigured, "app", "http", false, new Dictionary<string, string>(), string.Empty, endpoint);
}
