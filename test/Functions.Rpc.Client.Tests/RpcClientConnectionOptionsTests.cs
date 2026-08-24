// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class RpcClientConnectionOptionsTests
{
    [Fact]
    public void ConstructorSetsValidatedValues()
    {
        Uri endpoint = new("https://localhost:5001");

        RpcClientConnectionOptions options = new(endpoint, "worker-1");

        Assert.Equal(endpoint, options.Endpoint);
        Assert.Equal("worker-1", options.WorkerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ConstructorRejectsEmptyWorkerId(string workerId)
    {
        Assert.Throws<ArgumentException>(() => new RpcClientConnectionOptions(new Uri("http://localhost"), workerId));
    }

    [Fact]
    public void ConstructorRejectsRelativeEndpoint()
    {
        Assert.Throws<ArgumentException>(() =>
            new RpcClientConnectionOptions(new Uri("relative", UriKind.Relative), "worker-1"));
    }

    [Theory]
    [InlineData("ftp://localhost")]
    [InlineData("file:///tmp/rpc.sock")]
    public void ConstructorRejectsUnsupportedEndpointScheme(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            new RpcClientConnectionOptions(new Uri(endpoint), "worker-1"));
    }

    [Theory]
    [InlineData("http://user:password@localhost")]
    [InlineData("http://localhost/functions")]
    [InlineData("http://localhost?query=value")]
    [InlineData("http://localhost#fragment")]
    public void ConstructorRejectsUnsupportedEndpointComponents(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            new RpcClientConnectionOptions(new Uri(endpoint), "worker-1"));
    }

    [Fact]
    public void HttpHandlerUsesTransportLivenessDefaults()
    {
        using SocketsHttpHandler handler = RpcClientConnection.CreateHttpHandler();

        Assert.Equal(RpcClientConnection.DefaultConnectTimeout, handler.ConnectTimeout);
        Assert.Equal(RpcClientConnection.DefaultKeepAlivePingDelay, handler.KeepAlivePingDelay);
        Assert.Equal(RpcClientConnection.DefaultKeepAlivePingTimeout, handler.KeepAlivePingTimeout);
        Assert.Equal(HttpKeepAlivePingPolicy.Always, handler.KeepAlivePingPolicy);
    }
}
