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

        Uri? destination = provider.FinalizeCapabilities(capabilities, "https://worker-pod.example:48801/");

        Assert.Equal(new Uri("http://localhost:1234/worker/"), destination);
        Assert.Equal("https://worker-pod.example:48801/", capabilities["HttpUri"]);
        Assert.Equal("true", capabilities["WorkerIndexing"]);
        Assert.Equal("case-sensitive-capability", capabilities["httpuri"]);
    }

    [Fact]
    public void FinalizeCapabilities_OverrideTakesPrecedenceOverAdvertisedDestination()
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { WorkerHttpEndpoint = " https://override:1234/prefix/ " });
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:5678" };

        Uri? destination = provider.FinalizeCapabilities(capabilities, "https://worker-pod:28080");

        Assert.Equal(new Uri("https://override:1234/prefix/"), destination);
        Assert.Equal("https://worker-pod:28080/", capabilities["HttpUri"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void FinalizeCapabilities_NoUsableHttpCapability_DoesNotAdvertiseProxying(string? advertisedEndpoint)
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { WorkerHttpEndpoint = "http://override:1234" });
        Dictionary<string, string> capabilities = new() { ["WorkerIndexing"] = "true" };
        if (advertisedEndpoint is not null)
        {
            capabilities["HttpUri"] = advertisedEndpoint;
        }

        Assert.Null(provider.FinalizeCapabilities(capabilities, "https://worker-pod:28080"));
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
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { WorkerHttpEndpoint = endpoint });
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:5678" };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => provider.FinalizeCapabilities(capabilities, "https://worker-pod:28080"));

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

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => provider.FinalizeCapabilities(capabilities, "https://worker-pod:28080"));

        Assert.Contains("HttpUri", exception.Message);
        Assert.Equal(advertisedEndpoint, capabilities["HttpUri"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void FinalizeCapabilities_MissingProxyEndpoint_RejectsLocalDestinationFallback(string? proxyEndpoint)
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new());
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = "http://localhost:5678" };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => provider.FinalizeCapabilities(capabilities, proxyEndpoint));

        Assert.Contains("httpProxyEndpoint", exception.Message);
        Assert.Equal("http://localhost:5678", capabilities["HttpUri"]);
    }

    [Fact]
    public void FinalizeCapabilities_UsesMatchingAcceptedMappedOriginForInitializationAndReload()
    {
        WorkerPodStateManager manager = new(
            Options.Create(new WorkerProxyOptions { PodName = "test-worker-pod" }), TimeProvider.System);
        manager.Assign(new(WorkerStartupMode.Preconfigured, "test-app", "group", false, new Dictionary<string, string>(), string.Empty,
            new Uri("https://127.0.0.1:48801/")));
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");
        WorkerHttpCapabilityProvider provider = CreateProvider(new());
        Dictionary<string, string> first = new() { ["HttpUri"] = "http://localhost:1234/customer/" };
        Dictionary<string, string> reload = new() { ["HttpUri"] = "http://localhost:5678/changed/" };
        (WorkerPodState snapshot, string? proxyEndpoint) = manager.GetWorkerCapabilitySnapshot(1);

        Assert.Equal(manager.State, snapshot);
        Assert.Equal(new Uri("http://localhost:1234/customer/"), provider.FinalizeCapabilities(first, proxyEndpoint));
        Assert.Equal(new Uri("http://localhost:5678/changed/"), provider.FinalizeCapabilities(reload, proxyEndpoint));
        Assert.Equal("https://127.0.0.1:48801/", first["HttpUri"]);
        Assert.Equal("https://127.0.0.1:48801/", reload["HttpUri"]);
        Assert.Null(manager.GetWorkerCapabilitySnapshot(2).HttpProxyEndpoint);
    }

    [Theory]
    [InlineData("https://worker-pod:28080", "https://worker-pod:28080", false)]
    [InlineData("https://WORKER-POD:28080", "https://worker-pod:28080", false)]
    [InlineData("https://worker-pod:443/worker/", "https://worker-pod/", false)]
    [InlineData("https://worker-pod/worker/", "https://WORKER-POD:443/proxy/", false)]
    [InlineData("https://worker-pod:28080", "https://worker-pod:28080", true)]
    [InlineData("https://WORKER-POD:28080", "https://worker-pod:28080", true)]
    [InlineData("https://worker-pod:443/worker/", "https://worker-pod/", true)]
    [InlineData("https://worker-pod/worker/", "https://WORKER-POD:443/proxy/", true)]
    public void FinalizeCapabilities_SameOrigin_FailsWithoutRewritingHttpUri(string endpoint, string proxyEndpoint, bool useOverride)
    {
        string advertisedEndpoint = useOverride ? "http://localhost:5678" : endpoint;
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { WorkerHttpEndpoint = useOverride ? endpoint : null });
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = advertisedEndpoint, ["WorkerIndexing"] = "true" };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => provider.FinalizeCapabilities(capabilities, proxyEndpoint));

        Assert.Contains("forwarding loop", exception.Message);
        Assert.Equal(advertisedEndpoint, capabilities["HttpUri"]);
        Assert.Equal("true", capabilities["WorkerIndexing"]);
    }

    [Theory]
    [InlineData("https://worker:28080", "https://proxy:28080", false)]
    [InlineData("https://worker:28080", "https://worker:28081", false)]
    [InlineData("http://worker:28080", "https://worker:28080", false)]
    [InlineData("https://worker:28080", "https://proxy:28080", true)]
    [InlineData("https://worker:28080", "https://worker:28081", true)]
    [InlineData("http://worker:28080", "https://worker:28080", true)]
    public void FinalizeCapabilities_DistinctOrigin_RewritesHttpUri(string endpoint, string proxyEndpoint, bool useOverride)
    {
        WorkerHttpCapabilityProvider provider = CreateProvider(new() { WorkerHttpEndpoint = useOverride ? endpoint : null });
        Dictionary<string, string> capabilities = new() { ["HttpUri"] = useOverride ? proxyEndpoint : endpoint };

        Uri? destination = provider.FinalizeCapabilities(capabilities, proxyEndpoint);

        Assert.Equal(new Uri(endpoint), destination);
        Assert.Equal(new Uri(proxyEndpoint).AbsoluteUri, capabilities["HttpUri"]);
    }

    private static WorkerHttpCapabilityProvider CreateProvider(WorkerProxyOptions options) =>
        new(Options.Create(options), NullLogger<WorkerHttpCapabilityProvider>.Instance);
}
