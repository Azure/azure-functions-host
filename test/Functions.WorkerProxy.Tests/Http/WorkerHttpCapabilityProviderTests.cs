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

public class WorkerHttpCapabilityProviderTests
{
    [Fact]
    public void FinalizeCapabilities_CapturesDestinationBeforeRewritingHttpUri()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new());
        Dictionary<string, string> capabilities = new()
        {
            ["HttpUri"] = " http://localhost:1234/worker/ ",
            ["WorkerIndexing"] = "true",
            ["httpuri"] = "case-sensitive-capability"
        };

        Uri? destination = provider.FinalizeCapabilities(capabilities, CreateAssignment("worker-pod.example:48801"));

        Assert.Equal(new Uri("http://localhost:1234/worker/"), destination);
        Assert.Equal("http://worker-pod.example:48801/", capabilities["HttpUri"]);
        Assert.Equal("true", capabilities["WorkerIndexing"]);
        Assert.Equal("case-sensitive-capability", capabilities["httpuri"]);
    }

    [Fact]
    public void FinalizeCapabilities_OverrideTakesPrecedenceOverAdvertisedDestination()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new()
        {
            WorkerHttpEndpoint = " https://override:1234/prefix/ "
        });
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:5678" };

        Uri? destination = provider.FinalizeCapabilities(capabilities, CreateAssignment());

        Assert.Equal(new Uri("https://override:1234/prefix/"), destination);
        Assert.Equal("http://worker-pod:28080/", capabilities["HttpUri"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void FinalizeCapabilities_NoUsableHttpCapability_DoesNotAdvertiseProxying(string? advertisedEndpoint)
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new()
        {
            WorkerHttpEndpoint = "http://override:1234"
        });
        Dictionary<string, string> capabilities = new() { ["WorkerIndexing"] = "true" };
        if (advertisedEndpoint is not null)
        {
            capabilities["HttpUri"] = advertisedEndpoint;
        }

        Assert.Null(provider.FinalizeCapabilities(capabilities, CreateAssignment()));
        Assert.False(capabilities.ContainsKey("HttpUri"));
        Assert.Equal("true", capabilities["WorkerIndexing"]);
    }

    [Theory]
    [InlineData("ftp://override:1234")]
    [InlineData("http://override:0")]
    [InlineData("http://user@override:1234")]
    [InlineData("http://override:1234/prefix?name=value")]
    [InlineData("http://override:1234/prefix#fragment")]
    public void FinalizeCapabilities_InvalidOverride_FailsInsteadOfFallingBack(string endpoint)
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new()
        {
            WorkerHttpEndpoint = endpoint
        });
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:5678" };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities, CreateAssignment()));

        Assert.Contains(nameof(WorkerProxyOptions.WorkerHttpEndpoint), exception.Message);
        Assert.Equal("http://localhost:5678", capabilities["HttpUri"]);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("ftp://localhost:1234")]
    [InlineData("http://localhost:invalid")]
    [InlineData("http://localhost:0")]
    [InlineData("http://user@localhost:1234")]
    [InlineData("http://localhost:1234/worker?name=value")]
    [InlineData("http://localhost:1234/worker#fragment")]
    public void FinalizeCapabilities_InvalidHttpCapability_FailsInsteadOfChangingInvocationTransport(string advertisedEndpoint)
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new());
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = advertisedEndpoint };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities, CreateAssignment()));

        Assert.Contains("HttpUri", exception.Message);
        Assert.Equal(advertisedEndpoint, capabilities["HttpUri"]);
    }

    [Fact]
    public void FinalizeCapabilities_MissingAssignment_FailsInsteadOfAdvertisingWorkerLoopback()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new());
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:5678" };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities, assignment: null));

        Assert.Contains("accepted worker assignment", exception.Message);
        Assert.Equal("http://localhost:5678", capabilities["HttpUri"]);
    }

    [Theory]
    [InlineData("mapped.example:443", "http", "http://mapped.example:443/")]
    [InlineData("mapped.example:80", "https", "https://mapped.example:80/")]
    [InlineData("[::1]:49187", "http", "http://[::1]:49187/")]
    public void AssignedEndpoint_ComposesOriginUsingListenerScheme(string endpoint, string scheme, string origin)
    {
        WorkerAssignment assignment = CreateAssignment(endpoint);

        Assert.Equal(origin, WorkerProxyOptionsValidator.ResolveHttpProxyOrigin(assignment.HttpProxyEndpoint, scheme)?.AbsoluteUri);
        Assert.Equal(endpoint, assignment.HttpProxyEndpoint);
    }

    [Fact]
    public void FinalizeCapabilities_AssignedOriginCannotLoopBackToWorkerDestination()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { HttpPort = 28080 });
        WorkerAssignment assignment = new(WorkerStartupMode.Preconfigured, "app", "http", false,
            new Dictionary<string, string>(), string.Empty, "localhost:1234");
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:1234/worker/" };

        Assert.Throws<InvalidOperationException>(() => provider.FinalizeCapabilities(capabilities, assignment));
        Assert.Equal("http://localhost:1234/worker/", capabilities["HttpUri"]);
    }

    private static WorkerHttpCapabilityProvider CreateProvider(WorkerProxyOptions options)
    {
        WorkerProxyEndpointConfiguration endpoints = new(Options.Create(options));
        endpoints.Configure(new());
        return new(Options.Create(options), endpoints, NullLogger<WorkerHttpCapabilityProvider>.Instance);
    }

    private static WorkerAssignment CreateAssignment(string endpoint = "worker-pod:28080")
        => new(WorkerStartupMode.Preconfigured, "app", "http", false, new Dictionary<string, string>(), string.Empty, endpoint);
}
