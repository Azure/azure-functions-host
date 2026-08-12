// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.WorkerProxy;
using Azure.Functions.WorkerProxy.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Configuration;

[Collection(nameof(EnvironmentVariableCollection))]
public class WorkerProxyOptionsTests
{
    [Fact]
    public void Options_UseDefault_WhenNoValueIsConfigured()
    {
        using EnvironmentVariableScope managementPort =
            new(WorkerProxyOptions.ManagementPortConfigurationKey, value: null);

        WorkerProxyOptions options = GetOptions();

        Assert.Equal(WorkerProxyOptions.DefaultManagementPort, options.ManagementPort);
    }

    [Fact]
    public void Options_UseEnvironmentValue()
    {
        using EnvironmentVariableScope managementPort =
            new(WorkerProxyOptions.ManagementPortConfigurationKey, "8080");

        WorkerProxyOptions options = GetOptions();

        Assert.Equal(8080, options.ManagementPort);
    }

    [Theory]
    [InlineData("--management-port", "9090")]
    [InlineData("--management-port=9090")]
    public void Options_UseCommandLineValue(params string[] arguments)
    {
        using EnvironmentVariableScope managementPort =
            new(WorkerProxyOptions.ManagementPortConfigurationKey, value: null);

        WorkerProxyOptions options = GetOptions(arguments);

        Assert.Equal(9090, options.ManagementPort);
    }

    [Fact]
    public void Options_CommandLineOverridesEnvironment()
    {
        using EnvironmentVariableScope managementPort =
            new(WorkerProxyOptions.ManagementPortConfigurationKey, "8080");

        WorkerProxyOptions options = GetOptions("--management-port", "9090");

        Assert.Equal(9090, options.ManagementPort);
    }

    [Fact]
    public void Options_LastCommandLineValueWins()
    {
        using EnvironmentVariableScope managementPort =
            new(WorkerProxyOptions.ManagementPortConfigurationKey, value: null);

        WorkerProxyOptions options =
            GetOptions("--management-port", "8080", "--management-port=9090");

        Assert.Equal(9090, options.ManagementPort);
    }

    [Fact]
    public void Options_IgnoreUnknownArguments()
    {
        using EnvironmentVariableScope managementPort =
            new(WorkerProxyOptions.ManagementPortConfigurationKey, value: null);

        WorkerProxyOptions options = GetOptions("--unknown", "value");

        Assert.Equal(WorkerProxyOptions.DefaultManagementPort, options.ManagementPort);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData(" ")]
    public void Options_Throw_WhenValueIsNotAnInteger(string value)
    {
        using EnvironmentVariableScope managementPort =
            new(WorkerProxyOptions.ManagementPortConfigurationKey, value: null);

        Assert.Throws<InvalidOperationException>(
            () => WorkerProxyApplication.Build(["--management-port", value]));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("65536")]
    public void Options_Throw_WhenPortIsOutOfRange(string value)
    {
        using EnvironmentVariableScope managementPort =
            new(WorkerProxyOptions.ManagementPortConfigurationKey, value: null);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => WorkerProxyApplication.Build(["--management-port", value]));

        Assert.Contains("between 1 and 65535", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--management-port")]
    [InlineData("--management-port", "")]
    [InlineData("--management-port=")]
    public void Options_UseDefault_WhenCommandLineValueIsEmpty(params string[] arguments)
    {
        using EnvironmentVariableScope managementPort =
            new(WorkerProxyOptions.ManagementPortConfigurationKey, value: null);

        WorkerProxyOptions options = GetOptions(arguments);

        Assert.Equal(WorkerProxyOptions.DefaultManagementPort, options.ManagementPort);
    }

    private static WorkerProxyOptions GetOptions(params string[] arguments)
    {
        using WebApplication app = WorkerProxyApplication.Build(arguments);

        return app.Services.GetRequiredService<IOptions<WorkerProxyOptions>>().Value;
    }
}
