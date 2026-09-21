// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.WorkerProxy.Http;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerHttpDestinationResolverTests
{
    [Fact]
    public void Resolve_ConfiguredAndAdvertisedEndpoints_ReturnsConfiguredEndpoint()
    {
        Uri? destination = WorkerHttpDestinationResolver.Resolve(
            " http://override:1234 ",
            "http://advertised:5678");

        Assert.Equal(new Uri("http://override:1234"), destination);
    }

    [Theory]
    [InlineData(null, "http://advertised:5678", "http://advertised:5678/")]
    [InlineData(" ", "https://advertised:5678/path", "https://advertised:5678/path")]
    [InlineData(null, "http://100.64.1.12:48801", "http://100.64.1.12:48801/")]
    [InlineData(null, "http://[fd00::12]:48801/worker/", "http://[fd00::12]:48801/worker/")]
    [InlineData(null, "https://advertised/prefix/", "https://advertised/prefix/")]
    public void Resolve_NoUsableOverride_ReturnsAdvertisedEndpoint(
        string? overrideEndpoint,
        string advertisedEndpoint,
        string expected)
    {
        Uri? destination = WorkerHttpDestinationResolver.Resolve(overrideEndpoint, advertisedEndpoint);

        Assert.Equal(new Uri(expected), destination);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "relative")]
    [InlineData("ftp://worker:1234", " ")]
    public void Resolve_NoUsableEndpoint_ReturnsNull(string? overrideEndpoint, string? advertisedEndpoint)
    {
        Assert.Null(WorkerHttpDestinationResolver.Resolve(overrideEndpoint, advertisedEndpoint));
    }

    [Theory]
    [InlineData("http://worker:0")]
    [InlineData("http://user@worker:1234")]
    [InlineData("http://worker:1234/prefix?name=value")]
    [InlineData("http://worker:1234/prefix#fragment")]
    public void Resolve_InvalidDestinationPrefix_ReturnsNullForAdvertisedEndpointAndOverride(string endpoint)
    {
        Assert.Null(WorkerHttpDestinationResolver.Resolve(overrideEndpoint: null, endpoint));
        Assert.Null(WorkerHttpDestinationResolver.Resolve(endpoint, "http://advertised:5678"));
    }

    [Fact]
    public void Resolve_InvalidConfiguredEndpoint_DoesNotFallBackToAdvertisedEndpoint()
    {
        Assert.Null(WorkerHttpDestinationResolver.Resolve(
            "ftp://override:1234",
            "http://advertised:5678"));
    }
}
