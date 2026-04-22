// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerProxy;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class WorkerHttpDestinationResolverTests
{
    // The override (--worker-http-endpoint, CLI or env var) is the operator's explicit
    // pin. When supplied it must win regardless of what the worker advertises — that's
    // the whole point of the override (Aspire dev harness, integration tests pointing
    // at a stub, deployments where the operator wants a fixed destination).
    [Fact]
    public void Resolve_OverrideAndAdvertisedBothSet_ReturnsOverride()
    {
        var result = WorkerHttpDestinationResolver.Resolve(
            overrideEndpoint: "http://override:1234",
            advertisedEndpoint: "http://localhost:5001");

        Assert.Equal("http://override:1234", result);
    }

    [Fact]
    public void Resolve_OnlyOverrideSet_ReturnsOverride()
    {
        var result = WorkerHttpDestinationResolver.Resolve(
            overrideEndpoint: "http://override:1234",
            advertisedEndpoint: null);

        Assert.Equal("http://override:1234", result);
    }

    // The common production path: no override is supplied, so the worker-advertised
    // dynamic port (captured from WorkerInitResponse / FunctionEnvironmentReloadResponse
    // capabilities) is the destination.
    [Fact]
    public void Resolve_OnlyAdvertisedSet_ReturnsAdvertised()
    {
        var result = WorkerHttpDestinationResolver.Resolve(
            overrideEndpoint: null,
            advertisedEndpoint: "http://localhost:5001");

        Assert.Equal("http://localhost:5001", result);
    }

    [Fact]
    public void Resolve_NeitherSet_ReturnsNull()
    {
        var result = WorkerHttpDestinationResolver.Resolve(
            overrideEndpoint: null,
            advertisedEndpoint: null);

        Assert.Null(result);
    }

    // Defensive: an environment variable that's been set to an empty/whitespace string
    // (a real-world scenario in container orchestrators that always inject the variable
    // even when empty) must NOT shadow a perfectly good worker-advertised URI. Treating
    // a blank override as "no override supplied" lets the proxy fall through to the
    // worker's dynamic port instead of 503ing.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Resolve_BlankOverride_FallsThroughToAdvertised(string blankOverride)
    {
        var result = WorkerHttpDestinationResolver.Resolve(
            overrideEndpoint: blankOverride,
            advertisedEndpoint: "http://localhost:5001");

        Assert.Equal("http://localhost:5001", result);
    }

    // Defensive: a worker that advertised a blank HttpUri (or the field never being set)
    // is treated as "no advertised destination" so we don't try to forward to "".
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankAdvertised_NotUsed(string blankAdvertised)
    {
        var result = WorkerHttpDestinationResolver.Resolve(
            overrideEndpoint: null,
            advertisedEndpoint: blankAdvertised);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_BothBlank_ReturnsNull()
    {
        var result = WorkerHttpDestinationResolver.Resolve(
            overrideEndpoint: "  ",
            advertisedEndpoint: "");

        Assert.Null(result);
    }

    // Whitespace-padded values from env-var templating (Helm, docker-compose, k8s
    // ConfigMap) must be trimmed before being handed to YARP — otherwise the forwarder
    // throws UriFormatException at request time and the operator sees 500s instead of
    // either the 503 they'd get from a missing destination or the successful forwarding
    // they intended.
    [Theory]
    [InlineData("  http://override:1234", "http://override:1234")]
    [InlineData("http://override:1234  ", "http://override:1234")]
    [InlineData("\thttp://override:1234\n", "http://override:1234")]
    public void Resolve_PaddedOverride_IsTrimmed(string paddedOverride, string expected)
    {
        var result = WorkerHttpDestinationResolver.Resolve(
            overrideEndpoint: paddedOverride,
            advertisedEndpoint: null);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("  http://localhost:5001", "http://localhost:5001")]
    [InlineData("http://localhost:5001\r\n", "http://localhost:5001")]
    public void Resolve_PaddedAdvertised_IsTrimmed(string paddedAdvertised, string expected)
    {
        var result = WorkerHttpDestinationResolver.Resolve(
            overrideEndpoint: null,
            advertisedEndpoint: paddedAdvertised);

        Assert.Equal(expected, result);
    }
}
