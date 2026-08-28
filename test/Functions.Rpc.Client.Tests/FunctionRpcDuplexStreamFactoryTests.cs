// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public class FunctionRpcDuplexStreamFactoryTests
{
    [Fact]
    public void ValidateEndpointAcceptsHttpAuthority()
    {
        FunctionRpcDuplexStreamFactory.ValidateEndpoint(new Uri("https://localhost:5001"));
    }

    [Fact]
    public void ValidateEndpointRejectsRelativeUri()
    {
        Assert.Throws<ArgumentException>(() =>
            FunctionRpcDuplexStreamFactory.ValidateEndpoint(new Uri("relative", UriKind.Relative)));
    }

    [Theory]
    [InlineData("ftp://localhost")]
    [InlineData("file:///tmp/rpc.sock")]
    public void ValidateEndpointRejectsUnsupportedScheme(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            FunctionRpcDuplexStreamFactory.ValidateEndpoint(new Uri(endpoint)));
    }

    [Theory]
    [InlineData("http://localhost/functions")]
    [InlineData("http://localhost?query=value")]
    [InlineData("http://localhost#fragment")]
    public void ValidateEndpointRejectsCallSpecificComponents(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            FunctionRpcDuplexStreamFactory.ValidateEndpoint(new Uri(endpoint)));
    }

    [Fact]
    public void ValidateEndpointRejectsUserInformation()
    {
        Uri endpoint = new UriBuilder(Uri.UriSchemeHttp, "localhost")
        {
            UserName = "user",
        }.Uri;

        Assert.Throws<ArgumentException>(() => FunctionRpcDuplexStreamFactory.ValidateEndpoint(endpoint));
    }

    [Fact]
    public void HttpHandlerUsesTransportLivenessDefaults()
    {
        using SocketsHttpHandler handler = FunctionRpcDuplexStreamFactory.CreateHttpHandler();

        Assert.Equal(TimeSpan.FromSeconds(5), handler.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), handler.KeepAlivePingDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), handler.KeepAlivePingTimeout);
        Assert.Equal(HttpKeepAlivePingPolicy.Always, handler.KeepAlivePingPolicy);
    }

    private static FunctionRpcDuplexStreamFactory CreateFactory()
        => new(NullLogger<FunctionRpcDuplexStreamFactory>.Instance);
}
