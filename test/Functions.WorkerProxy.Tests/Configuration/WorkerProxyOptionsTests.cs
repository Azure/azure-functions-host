// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.WorkerProxy;
using Azure.Functions.WorkerProxy.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Configuration;

public class WorkerProxyOptionsTests
{
    [Fact]
    public void Options_UseDefault_WhenNoValueIsConfigured()
    {
        WorkerProxyOptions options = GetOptions();

        Assert.Equal(WorkerProxyOptions.DefaultManagementPort, options.ManagementPort);
    }

    [Fact]
    public void Options_UseConfiguredValue()
    {
        WorkerProxyOptions options = GetOptions(
            configuredManagementPort: "8080");

        Assert.Equal(8080, options.ManagementPort);
    }

    [Theory]
    [InlineData("--management-port", "9090")]
    [InlineData("--management-port=9090")]
    public void Options_UseCommandLineValue(params string[] arguments)
    {
        WorkerProxyOptions options = GetOptions(arguments: arguments);

        Assert.Equal(9090, options.ManagementPort);
    }

    [Fact]
    public void Options_CommandLineOverridesConfiguredValue()
    {
        WorkerProxyOptions options = GetOptions(
            configuredManagementPort: "8080",
            arguments: ["--management-port", "9090"]);

        Assert.Equal(9090, options.ManagementPort);
    }

    [Fact]
    public void Options_LastCommandLineValueWins()
    {
        WorkerProxyOptions options = GetOptions(
            arguments: ["--management-port", "8080", "--management-port=9090"]);

        Assert.Equal(9090, options.ManagementPort);
    }

    [Fact]
    public void Options_IgnoreUnknownArguments()
    {
        WorkerProxyOptions options = GetOptions(arguments: ["--unknown", "value"]);

        Assert.Equal(WorkerProxyOptions.DefaultManagementPort, options.ManagementPort);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData(" ")]
    public void Options_Throw_WhenValueIsNotAnInteger(string value)
    {
        Assert.Throws<InvalidOperationException>(
            () => BuildApplication(arguments: ["--management-port", value]));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("65536")]
    public void Options_Throw_WhenPortIsOutOfRange(string value)
    {
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => BuildApplication(arguments: ["--management-port", value]));

        Assert.Contains("between 1 and 65535", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--management-port")]
    [InlineData("--management-port", "")]
    [InlineData("--management-port=")]
    public void Options_UseDefault_WhenCommandLineValueIsEmpty(params string[] arguments)
    {
        WorkerProxyOptions options = GetOptions(arguments: arguments);

        Assert.Equal(WorkerProxyOptions.DefaultManagementPort, options.ManagementPort);
    }

    private static WebApplication BuildApplication(
        string? configuredManagementPort = null, string[]? arguments = null)
    {
        return WorkerProxyApplication.Build(configuration =>
        {
            if (configuredManagementPort is not null)
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [WorkerProxyOptions.ManagementPortConfigurationKey] =
                            configuredManagementPort
                    });
            }

            WorkerProxyApplication.AddCommandLineConfiguration(configuration, arguments ?? []);
        });
    }

    private static WorkerProxyOptions GetOptions(
        string? configuredManagementPort = null, string[]? arguments = null)
    {
        using WebApplication app = BuildApplication(configuredManagementPort, arguments);

        return app.Services.GetRequiredService<IOptions<WorkerProxyOptions>>().Value;
    }
}
