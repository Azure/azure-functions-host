// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.WorkerProxy.Configuration;
using Microsoft.Azure.Functions.WorkerProxy.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class MsFunctionLogsLoggerTests
{
    private static readonly Regex TraceEventRegex = new(
        "^MS_FUNCTION_LOGS (?<Level>[0-6]),(?<SubscriptionId>[^,]*),(?<AppName>[^,]*),(?<FunctionName>[^,]*),(?<EventName>[^,]*),(?<Source>[^,]*),\"(?<Details>.*)\",\"(?<Summary>.*)\",(?<HostVersion>[^,]*),(?<EventTimestamp>[^,]+),(?<ExceptionType>[^,]*),\"(?<ExceptionMessage>.*)\",(?<FunctionInvocationId>[^,]*),(?<HostInstanceId>[^,]*),(?<ActivityId>[^,\"]*),(?<ContainerName>[^,\"]*),(?<StampName>[^,\"]*),(?<TenantId>[^,\"]*),(?<RuntimeSiteName>[^,]*),(?<SlotName>[^,]*),(?<Pid>[^,\"]*)$",
        RegexOptions.Compiled);

    [Fact]
    public void Log_WritesRuntimeCompatibleLine()
    {
        var lines = new List<string>();
        using var provider = new MsFunctionLogsLoggerProvider(lines.Add, CreateOptions("container-01", "Stamp-01", "TENANT-01"));
        ILogger logger = provider.CreateLogger("Microsoft.Azure.Functions.WorkerProxy.FunctionRpcRelay");
        string summaryValue = "line1\r\"cr\"\n\"lf\"\r\n\"crlf\"";
        var exception = new InvalidOperationException("bad\r\"cr\"\n\"lf\"\r\n\"crlf\"");

        logger.LogError(exception, "summary {Value}", summaryValue);

        string line = Assert.Single(lines);
        Assert.DoesNotContain('\r', line);
        Assert.DoesNotContain('\n', line);

        Match match = TraceEventRegex.Match(line);

        Assert.True(match.Success);
        Assert.Equal("2", match.Groups["Level"].Value);
        Assert.Equal("Microsoft.Azure.Functions.WorkerProxy.FunctionRpcRelay", match.Groups["Source"].Value);
        Assert.Equal(MsFunctionLogsLogger.NormalizeString(exception.ToString(), addEnclosingQuotes: false), match.Groups["Details"].Value);
        Assert.Equal(MsFunctionLogsLogger.NormalizeString($"summary {summaryValue}", addEnclosingQuotes: false), match.Groups["Summary"].Value);
        Assert.Equal("CONTAINER-01", match.Groups["ContainerName"].Value);
        Assert.Equal("stamp-01", match.Groups["StampName"].Value);
        Assert.Equal("tenant-01", match.Groups["TenantId"].Value);
        Assert.Equal(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), match.Groups["Pid"].Value);
        Assert.Empty(match.Groups["SubscriptionId"].Value);
        Assert.Empty(match.Groups["AppName"].Value);
        Assert.Empty(match.Groups["FunctionName"].Value);
        Assert.Empty(match.Groups["EventName"].Value);
        Assert.Empty(match.Groups["HostVersion"].Value);
        Assert.Empty(match.Groups["ExceptionType"].Value);
        Assert.Empty(match.Groups["ExceptionMessage"].Value);
        Assert.Empty(match.Groups["FunctionInvocationId"].Value);
        Assert.Empty(match.Groups["HostInstanceId"].Value);
        Assert.Empty(match.Groups["ActivityId"].Value);
        Assert.Empty(match.Groups["RuntimeSiteName"].Value);
        Assert.Empty(match.Groups["SlotName"].Value);
        Assert.True(DateTime.TryParse(match.Groups["EventTimestamp"].Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
    }

    [Fact]
    public void Log_LeavesRuntimeContextFieldsEmptyWhenUnset()
    {
        var lines = new List<string>();
        using var provider = new MsFunctionLogsLoggerProvider(lines.Add, CreateOptions());
        ILogger logger = provider.CreateLogger("Category");

        logger.LogInformation("hello");

        Match match = TraceEventRegex.Match(Assert.Single(lines));

        Assert.True(match.Success);
        Assert.Empty(match.Groups["ContainerName"].Value);
        Assert.Empty(match.Groups["StampName"].Value);
        Assert.Empty(match.Groups["TenantId"].Value);
    }

    [Theory]
    [InlineData(LogLevel.Trace, 5)]
    [InlineData(LogLevel.Debug, 5)]
    [InlineData(LogLevel.Information, 4)]
    [InlineData(LogLevel.Warning, 3)]
    [InlineData(LogLevel.Error, 2)]
    [InlineData(LogLevel.Critical, 1)]
    [InlineData(LogLevel.None, 0)]
    public void ToEventLevel_MapsExpectedValues(LogLevel level, int expected)
    {
        Assert.Equal(expected, MsFunctionLogsLogger.ToEventLevel(level));
    }

    [Fact]
    public void NormalizeString_ReplacesNewLinesAndDoubleQuotes()
    {
        string value = "line1\r\"line2\"\n\"line3\"\r\n\"line4\"";

        Assert.Equal("\"line1 'line2' 'line3' 'line4'\"", MsFunctionLogsLogger.NormalizeString(value));
        Assert.Equal("line1 'line2' 'line3' 'line4'", MsFunctionLogsLogger.NormalizeString(value, addEnclosingQuotes: false));
    }

    [Fact]
    public void Configure_NormalizesWellKnownValues()
    {
        var options = new WorkerProxyEnvironmentOptions();

        new WorkerProxyEnvironmentOptionsSetup(CreateConfiguration("container-01", "Stamp-01", "TENANT-01", "legion-host", "machine-01"))
            .Configure(options);

        Assert.Equal("CONTAINER-01", options.ContainerName);
        Assert.Equal("stamp-01", options.StampName);
        Assert.Equal("tenant-01", options.TenantId);
        Assert.Equal("legion-host", options.LegionServiceHost);
        Assert.Equal("machine-01", options.ComputerName);
        Assert.True(options.IsFlexOrLegion);
    }

    [Fact]
    public void Configure_LegionServiceHostEnablesFlexDetectionWithoutContainerName()
    {
        var options = new WorkerProxyEnvironmentOptions();

        new WorkerProxyEnvironmentOptionsSetup(CreateConfiguration(legionServiceHost: "legion-host"))
            .Configure(options);

        Assert.True(options.IsFlexOrLegion);
    }

    private static IOptions<WorkerProxyEnvironmentOptions> CreateOptions(
        string? containerName = null,
        string? stampName = null,
        string? tenantId = null,
        string? legionServiceHost = null,
        string? computerName = null)
    {
        var options = new WorkerProxyEnvironmentOptions();

        new WorkerProxyEnvironmentOptionsSetup(CreateConfiguration(containerName, stampName, tenantId, legionServiceHost, computerName))
            .Configure(options);

        return Options.Create(options);
    }

    private static IConfiguration CreateConfiguration(
        string? containerName = null,
        string? stampName = null,
        string? tenantId = null,
        string? legionServiceHost = null,
        string? computerName = null)
    {
        Dictionary<string, string?> values = new()
        {
            ["CONTAINER_NAME"] = containerName,
            ["WEBSITE_HOME_STAMPNAME"] = stampName,
            ["WEBSITE_STAMP_DEPLOYMENT_ID"] = tenantId,
            ["LEGION_SERVICE_HOST"] = legionServiceHost,
            ["COMPUTERNAME"] = computerName
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
