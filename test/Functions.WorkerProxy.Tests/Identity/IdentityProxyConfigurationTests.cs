// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Identity;

public sealed class IdentityProxyConfigurationTests
{
    [Fact]
    public void Options_UseStableDefaults()
    {
        IdentityProxyOptions options = new();

        Assert.Equal(8081, options.Port);
        Assert.Equal("/msi/token", options.TokenPath);
    }

    [Fact]
    public async Task DisabledProxy_DoesNotValidateUnusedSettingsOrCreateIdentityListener()
    {
        Dictionary<string, string?> configuration = new()
        {
            ["WorkerProxy:IdentityProxy:Port"] = "-1",
            ["WorkerProxy:IdentityProxy:TokenPath"] = "not-a-path",
            ["IDENTITY_HEADER"] = null
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        IdentityProxyOptions options = factory.Services.GetRequiredService<IOptions<IdentityProxyOptions>>().Value;
        WorkerProxyEndpointConfiguration endpoints = factory.Services.GetRequiredService<WorkerProxyEndpointConfiguration>();

        Assert.False(options.Enabled);
        Assert.Throws<InvalidOperationException>(() => endpoints.GetIdentityAddress());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://198.51.100.1:54321/another-path")]
    public async Task Listener_UsesConfiguredLoopbackPort_WithoutDependingOnIdentityEndpoint(string? endpoint)
    {
        Dictionary<string, string?> configuration = new()
        {
            ["WorkerProxy:IdentityProxy:Enabled"] = "true",
            ["WorkerProxy:IdentityProxy:Port"] = "0",
            ["IDENTITY_HEADER"] = "per-pod-secret",
            ["IDENTITY_ENDPOINT"] = endpoint
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        WorkerProxyEndpointConfiguration endpoints = factory.Services.GetRequiredService<WorkerProxyEndpointConfiguration>();
        Uri address = endpoints.GetIdentityAddress();

        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(address.Host)));
        Assert.Equal(Uri.UriSchemeHttp, address.Scheme);
        Assert.NotEqual(54321, address.Port);
        Assert.True(endpoints.IsIdentityPort(address.Port));
        Assert.Equal(endpoint, factory.Services.GetRequiredService<IConfiguration>()["IDENTITY_ENDPOINT"]);

        using HttpClient client = new() { BaseAddress = address };
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        using HttpResponseMessage response = await client.GetAsync("/MSI/token", timeout.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Options_EnabledProxyRequiresInjectedIdentityHeader(string? secret)
    {
        Dictionary<string, string?> configuration = new()
        {
            ["WorkerProxy:IdentityProxy:Enabled"] = "true",
            ["WorkerProxy:IdentityProxy:Port"] = "0",
            ["IDENTITY_HEADER"] = secret
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => factory.Services);

        Assert.Contains("platform-injected IDENTITY_HEADER", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Port", "-1", "The identity proxy Port must be between 0 and 65535.")]
    [InlineData("Port", "65536", "The identity proxy Port must be between 0 and 65535.")]
    [InlineData("TokenPath", "relative", "The identity proxy TokenPath must specify a valid token-acquisition path.")]
    [InlineData("TokenPath", "/admin/worker", "The identity proxy TokenPath must specify a valid token-acquisition path.")]
    [InlineData("TokenPath", "/api/specialize", "The identity proxy TokenPath must specify a valid token-acquisition path.")]
    public async Task Options_ReportsInvalidListenerConfiguration(string propertyName, string value, string expectedFailure)
    {
        Dictionary<string, string?> configuration = new()
        {
            ["WorkerProxy:IdentityProxy:Enabled"] = "true",
            ["WorkerProxy:IdentityProxy:Port"] = "0",
            ["IDENTITY_HEADER"] = "per-pod-secret",
            [$"WorkerProxy:IdentityProxy:{propertyName}"] = value
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => factory.Services);

        Assert.Equal(expectedFailure, Assert.Single(error.Failures));
    }

    [Fact]
    public async Task Options_ReportsAllIdentityConfigurationFailures()
    {
        Dictionary<string, string?> configuration = new()
        {
            ["WorkerProxy:IdentityProxy:Enabled"] = "true",
            ["WorkerProxy:IdentityProxy:Port"] = "-1",
            ["WorkerProxy:IdentityProxy:TokenPath"] = "not-a-path",
            ["IDENTITY_HEADER"] = null
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => factory.Services);

        Assert.Equal(
        [
            "The identity proxy Port must be between 0 and 65535.",
            "The identity proxy TokenPath must specify a valid token-acquisition path.",
            "The enabled identity proxy requires the platform-injected IDENTITY_HEADER."
        ], error.Failures);
    }

    [Theory]
    [InlineData("MaxRequestBodyBytes", "0", "MaxRequestBodyBytes must be greater than zero.")]
    [InlineData("MaxResponseBodyBytes", "-1", "MaxResponseBodyBytes must be greater than zero.")]
    [InlineData("MaxHeaderBytes", "0", "MaxHeaderBytes must be greater than zero.")]
    [InlineData("MaxHeaderCount", "0", "MaxHeaderCount must be greater than zero.")]
    [InlineData("MaxTargetBytes", "0", "MaxTargetBytes must be greater than zero.")]
    [InlineData("MaxPendingRequests", "0", "MaxPendingRequests must be greater than zero.")]
    [InlineData("MaxHostRequests", "0", "MaxHostRequests must be greater than zero.")]
    [InlineData("MaxBufferedBytes", "1", "MaxBufferedBytes must accommodate one request/response buffer reservation")]
    [InlineData("MaxHostBufferedBytes", "1", "MaxHostBufferedBytes must accommodate one request/response buffer reservation")]
    [InlineData("RequestTimeout", "00:00:00", "RequestTimeout must be greater than zero and no more than 2147483647 milliseconds.")]
    [InlineData("RequestTimeout", "30.00:00:00", "RequestTimeout must be greater than zero and no more than 2147483647 milliseconds.")]
    [InlineData("LinkTimeout", "00:00:00", "LinkTimeout must be greater than zero and no more than 2147483647 milliseconds.")]
    [InlineData("LinkTimeout", "30.00:00:00", "LinkTimeout must be greater than zero and no more than 2147483647 milliseconds.")]
    public async Task Options_ReportsTheRuleThatFailed(string propertyName, string value, string expectedFailure)
    {
        Dictionary<string, string?> configuration = new()
        {
            ["WorkerProxy:IdentityProxy:Enabled"] = "true",
            ["WorkerProxy:IdentityProxy:Port"] = "0",
            ["IDENTITY_HEADER"] = "per-pod-secret",
            [$"WorkerProxy:IdentityProxy:{propertyName}"] = value
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => factory.Services);

        Assert.StartsWith(expectedFailure, Assert.Single(error.Failures), StringComparison.Ordinal);
    }
}
