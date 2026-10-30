// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Diagnostics;

public partial class WorkerSystemLoggingTests
{
    [Fact]
    public async Task ConsoleLogging_PreservesCorrelationInExistingColumns()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.RelayCategory);
        using (logger.BeginScope(new WorkerSystemLogContext { WorkerId = "worker-id", ProxySessionId = 7 }))
        using (logger.BeginScope(new WorkerSystemLogContext
        {
            Operation = "Initialization",
            Stage = "ResponseReceived",
            Outcome = "Failed",
            ReasonCode = "WorkerFailure",
            DurationMs = 12.5,
            Attributes = new() { RpcStatus = "Failure", WorkerReady = false, StateRevision = 3 }
        }))
        {
            logger.LogWarning(new EventId(8, "ProtocolMessageObserved"), "Response received.");
        }

        logger.LogInformation(new EventId(1, "Unscoped"), "No invocation.");
        await generator.CompleteAsync();

        string[][] records = ParseCsvOutput(output.ToString());
        Assert.Equal(2, records.Length);
        Assert.All(records, record => Assert.Equal(21, record.Length));
        Assert.Equal(WorkerProxyLogExporter.RelayCategory, records[0][5]);
        Assert.Equal(string.Empty, records[0][12]);
        foreach (string field in new[]
        {
            "WorkerId=worker-id;", "ProxySessionId=7;", "Operation=Initialization;",
            "Stage=ResponseReceived;", "Outcome=Failed;", "ReasonCode=WorkerFailure;",
            "DurationMs=12.5;", "RpcStatus=Failure;", "WorkerReady=False;", "StateRevision=3;"
        })
        {
            Assert.Contains(field, records[0][7], StringComparison.Ordinal);
        }

        Assert.EndsWith("Response received.", records[0][7], StringComparison.Ordinal);
        Assert.Equal(string.Empty, records[1][12]);
        Assert.Equal("No invocation.", records[1][7]);
    }

    [Fact]
    public async Task ConsoleLogging_IgnoresArbitraryScopeProperties()
    {
        const string sentinel = "unapproved-scope-sentinel";
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.ApplicationCategory);
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["Authorization"] = sentinel,
            ["InvocationId"] = sentinel,
            ["OriginComponent"] = sentinel
        }))
        {
            logger.LogWarning("Proxy-owned diagnostic.");
        }

        await generator.CompleteAsync();

        string[] record = Assert.Single(ParseCsvOutput(output.ToString()));
        Assert.Equal("Proxy-owned diagnostic.", record[7]);
        Assert.Equal(string.Empty, record[12]);
        Assert.DoesNotContain(sentinel, output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("worker\ninjection")]
    [InlineData("worker,extra")]
    [InlineData("oversized")]
    public async Task ConsoleLogging_BoundsContextAndFingerprintsUnsafeWorkerIds(string workerId)
    {
        if (string.Equals(workerId, "oversized", StringComparison.Ordinal))
        {
            workerId = new string('w', 4_096);
        }

        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        string oversized = new('\u2603', 10_000);
        generator.LogEvent(LogLevel.Warning, "Failure", "Proxy", string.Empty, oversized,
            string.Empty, string.Empty, new()
            {
                WorkerId = workerId,
                ProxySessionId = 7,
                Operation = oversized,
                Stage = oversized,
                Outcome = oversized,
                ReasonCode = oversized
            });
        await generator.CompleteAsync();

        string[] record = Assert.Single(ParseCsvOutput(output.ToString()));
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(workerId)));
        Assert.Equal(21, record.Length);
        Assert.Contains($"WorkerId=sha256:{hash};", record[7], StringComparison.Ordinal);
        Assert.Contains("ProxySessionId=7;", record[7], StringComparison.Ordinal);
        Assert.Contains("ReasonCode=", record[7], StringComparison.Ordinal);
        Assert.Equal(string.Empty, record[12]);
        Assert.InRange(record[7].Length, 1, 1_024);
        Assert.DoesNotContain(workerId, output.ToString(), StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(output.ToString()), 1, 32_768);
    }

    [Fact]
    public async Task ConsoleLogging_UsesCapturedTimestampAndScopesDuringDeferredExport()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        using WorkerProxyLogExporter exporter = new(generator);
        using BatchLogRecordExportProcessor processor = new(exporter, scheduledDelayMilliseconds: 60_000);
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.AddProcessor(processor);
        }));
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.RelayCategory);
        DateTimeOffset before = DateTimeOffset.UtcNow;
        using (logger.BeginScope(new WorkerSystemLogContext { WorkerId = "worker-id" }))
        {
            logger.LogInformation(new EventId(8, "Deferred"), "Observed {Operation}.", "forwarding");
        }

        DateTimeOffset after = DateTimeOffset.UtcNow;
        using IDisposable? unrelated = logger.BeginScope(new WorkerSystemLogContext { WorkerId = "unrelated-worker" });
        Assert.True(processor.ForceFlush(5_000));
        await generator.CompleteAsync();

        string[] record = Assert.Single(ParseCsvOutput(output.ToString()));
        Assert.InRange(DateTimeOffset.Parse(record[9], CultureInfo.InvariantCulture), before, after);
        Assert.Contains("WorkerId=worker-id;", record[7], StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated-worker", record[7], StringComparison.Ordinal);
        Assert.EndsWith("Observed forwarding.", record[7], StringComparison.Ordinal);
        Assert.Equal(string.Empty, record[12]);
    }

    [Fact]
    public async Task LogExporter_UnsupportedForceFlushDoesNotStopLogging()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        using WorkerProxyLogExporter exporter = new(generator);
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter))));
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.ApplicationCategory);
        logger.LogInformation("Before flush.");
        Assert.False(exporter.ForceFlush(5_000));
        logger.LogInformation("After flush.");
        await generator.CompleteAsync();

        Assert.Equal(["Before flush.", "After flush."], ParseCsvOutput(output.ToString()).Select(record => record[7]));
    }

    [Fact]
    public async Task ConsoleLogging_ApplicationDisposalDrainsBufferedRecords()
    {
        StringWriter output = new();
        await using (WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
        {
            services.RemoveAll<IWorkerSystemEventGenerator>();
            services.AddSingleton<IWorkerSystemEventGenerator>(_ => new WorkerPodEventGenerator(output, TimeProvider.System));
        }))
        {
            using var client = factory.CreateWorkerProxyClient();
            ILogger logger = factory.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(WorkerProxyLogExporter.ApplicationCategory);
            for (int index = 0; index < 100; index++)
            {
                logger.LogInformation(new EventId(1, "Buffered"), "Record {Index}.", index);
            }
        }

        string[][] records = ParseCsvOutput(output.ToString());
        Assert.Equal(100, records.Count(record => string.Equals(record[4], "Buffered", StringComparison.Ordinal)));
        Assert.Contains(records, record => string.Equals(record[4], "WorkerProxyStopped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConsoleLogging_PreservesConcurrentEmissions()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() =>
            generator.LogEvent(LogLevel.Information, "Concurrent", "Proxy", string.Empty,
                index.ToString(CultureInfo.InvariantCulture), string.Empty, string.Empty))));
        await generator.CompleteAsync();

        string[][] records = ParseCsvOutput(output.ToString());
        Assert.Equal(100, records.Length);
        Assert.Equal(100, records.Select(record => record[7]).Distinct().Count());
    }
}
