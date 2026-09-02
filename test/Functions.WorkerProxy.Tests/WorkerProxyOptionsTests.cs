// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
            "--WorkerProxy:ManagementPort", "41000",
            "--WorkerProxy:RuntimeGrpcPort", "41001",
            "--WorkerProxy:WorkerGrpcPort", "41002",
            "--WorkerProxy:HttpPort", "41003",
            "--WorkerProxy:WorkerHttpEndpoint", "http://localhost:41004");

        Assert.Equal(41000, options.ManagementPort);
        Assert.Equal(41001, options.RuntimeGrpcPort);
        Assert.Equal(41002, options.WorkerGrpcPort);
        Assert.Equal(41003, options.HttpPort);
        Assert.Equal("http://localhost:41004", options.WorkerHttpEndpoint);
    }

    [Theory]
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
        using WebApplication app = WorkerProxyApplication.Build(args);

        return app.Services.GetRequiredService<IOptions<WorkerProxyOptions>>().Value;
    }
}
