// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.WorkerProxy.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerEndpointReadinessProbeOptionsTests
{
    [Fact]
    public void Options_UseStableDefaults()
    {
        WorkerEndpointReadinessProbeOptions options = GetOptions();

        Assert.Equal(TimeSpan.FromMilliseconds(25), options.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), options.TotalTimeout);
    }

    [Fact]
    public void Options_BindFromStandardConfiguration()
    {
        WorkerEndpointReadinessProbeOptions options = GetOptions(
            $"--{WorkerEndpointReadinessProbeOptions.SectionName}:RetryDelay", "00:00:00.010",
            $"--{WorkerEndpointReadinessProbeOptions.SectionName}:TotalTimeout", "00:00:02");

        Assert.Equal(TimeSpan.FromMilliseconds(10), options.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(2), options.TotalTimeout);
    }

    [Theory]
    [InlineData(nameof(WorkerEndpointReadinessProbeOptions.RetryDelay))]
    [InlineData(nameof(WorkerEndpointReadinessProbeOptions.TotalTimeout))]
    public void Options_RejectNonPositiveValues(string propertyName)
    {
        Assert.Throws<OptionsValidationException>(() => GetOptions(
            $"--{WorkerEndpointReadinessProbeOptions.SectionName}:{propertyName}", "00:00:00"));
    }

    private static WorkerEndpointReadinessProbeOptions GetOptions(params string[] args)
    {
        using WebApplication app = WorkerProxyApplication.Build(args);

        return app.Services.GetRequiredService<IOptions<WorkerEndpointReadinessProbeOptions>>().Value;
    }
}
