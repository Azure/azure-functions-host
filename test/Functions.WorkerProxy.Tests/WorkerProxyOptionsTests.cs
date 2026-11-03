// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.Tests;
using Azure.Functions.WorkerProxy.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerProxyOptionsTests
{
    [Fact]
    public void Options_UseStableDefaults()
    {
        WorkerProxyOptions options = GetOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), options.NeverLinkedTimeout);
        Assert.Equal(80, options.ManagementPort);
        Assert.Equal(50053, options.RuntimeGrpcPort);
        Assert.Equal(50054, options.WorkerGrpcPort);
        Assert.Equal(28080, options.HttpPort);
        Assert.Null(options.WorkerHttpEndpoint);
    }

    [Fact]
    public void Options_BindFromStandardConfiguration()
    {
        WorkerProxyOptions options = GetOptions(
            "--WEBSITE_POD_NAME", "configured-worker-pod",
            "--WorkerProxy:NeverLinkedTimeout", "00:02:00",
            "--WorkerProxy:ManagementPort", "41000",
            "--WorkerProxy:RuntimeGrpcPort", "41001",
            "--WorkerProxy:WorkerGrpcPort", "41002",
            "--WorkerProxy:HttpPort", "41003",
            "--WorkerProxy:WorkerHttpEndpoint", "http://localhost:41004");

        Assert.Equal("configured-worker-pod", options.PodName);
        Assert.Equal(TimeSpan.FromMinutes(2), options.NeverLinkedTimeout);
        Assert.Equal(41000, options.ManagementPort);
        Assert.Equal(41001, options.RuntimeGrpcPort);
        Assert.Equal(41002, options.WorkerGrpcPort);
        Assert.Equal(41003, options.HttpPort);
        Assert.Equal("http://localhost:41004", options.WorkerHttpEndpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative")]
    [InlineData("http://worker-pod:28080")]
    [InlineData("ftp://worker-pod:28080")]
    [InlineData("http://worker-pod:0")]
    [InlineData("http://worker-pod:28080/worker-id")]
    [InlineData("http://worker-pod:28080/?query=value")]
    [InlineData("http://worker-pod:28080/#fragment")]
    [InlineData("http://user:password@worker-pod:28080")]
    [InlineData("http://0.0.0.0:28080")]
    [InlineData("http://[::]:28080")]
    [InlineData("http://*:28080")]
    [InlineData("http://+:28080")]
    public void Assignment_RejectsInvalidAdvertisedHttpsOrigin(string endpoint)
    {
        Assert.False(WorkerHttpProxyEndpointResolver.TryResolve(endpoint, Uri.UriSchemeHttps, out _));
    }

    [Theory]
    [InlineData("https://localhost:28080")]
    [InlineData("https://worker-pod.example:48801/")]
    [InlineData("https://[::1]:28080")]
    [InlineData("https://100.64.1.12:48801")]
    [InlineData("https://[fd00::12]:48801")]
    public void Assignment_AllowsCompatibleHttpsOrigins(string endpoint)
    {
        Assert.True(WorkerHttpProxyEndpointResolver.TryResolve(endpoint, Uri.UriSchemeHttps, out Uri? resolved));
        Assert.Equal(new Uri(endpoint), resolved);
    }

    [Fact]
    public void Assignment_RejectsCredentialsInAdvertisedHttpsOrigin()
    {
        UriBuilder endpoint = new("https://worker-pod:28080") { UserName = "user" };

        Assert.False(WorkerHttpProxyEndpointResolver.TryResolve(endpoint.Uri.AbsoluteUri, Uri.UriSchemeHttps, out _));
    }

    [Theory]
    [InlineData(nameof(WorkerProxyOptions.NeverLinkedTimeout), "00:00:00")]
    [InlineData(nameof(WorkerProxyOptions.NeverLinkedTimeout), "50.00:00:00")]
    [InlineData(nameof(WorkerProxyOptions.ManagementPort), "-1")]
    [InlineData(nameof(WorkerProxyOptions.RuntimeGrpcPort), "65536")]
    [InlineData(nameof(WorkerProxyOptions.HttpPort), "-1")]
    public void Options_RejectOutOfRangeValues(string propertyName, string value)
    {
        string key = $"--{WorkerProxyOptions.SectionName}:{propertyName}";

        Assert.Throws<OptionsValidationException>(() => GetOptions(key, value));
    }

    [Fact]
    public void Options_RejectNonNumericValues()
    {
        Assert.Throws<InvalidOperationException>(() => GetOptions("--WorkerProxy:WorkerGrpcPort", "not-a-number"));
    }

    [Fact]
    public void Options_RejectDuplicateFixedPorts()
    {
        Assert.Throws<OptionsValidationException>(() => GetOptions(
            "--WorkerProxy:ManagementPort", "41000",
            "--WorkerProxy:RuntimeGrpcPort", "41000"));
    }

    [Fact]
    public void Options_AllowMultipleEphemeralPorts()
    {
        WorkerProxyOptions options = GetOptions(
            "--WorkerProxy:ManagementPort", "0",
            "--WorkerProxy:RuntimeGrpcPort", "0",
            "--WorkerProxy:WorkerGrpcPort", "0",
            "--WorkerProxy:HttpPort", "0");

        Assert.Equal(0, options.ManagementPort);
        Assert.Equal(0, options.RuntimeGrpcPort);
        Assert.Equal(0, options.WorkerGrpcPort);
        Assert.Equal(0, options.HttpPort);
    }

    private static WorkerProxyOptions GetOptions(params string[] args)
    {
        using TestCertificateFiles certificates = new();
        using WebApplication app = WorkerProxyApplication.Build(
            [.. certificates.Arguments, "--WEBSITE_POD_NAME", "test-worker-pod",
             "--CONTAINER_ENCRYPTION_KEY", RuntimeAuthenticationTestData.EncodedWorkerKey, .. args]);

        return app.Services.GetRequiredService<IOptions<WorkerProxyOptions>>().Value;
    }
}
