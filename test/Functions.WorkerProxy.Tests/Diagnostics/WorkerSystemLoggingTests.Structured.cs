// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
    public async Task StructuredLogging_PreservesCorrelationAndProxyOrigin()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System, useStructuredSystemLogs: true);
        using ILoggerFactory provider = CreateLoggerFactory(generator);
        ILogger proxy = provider.CreateLogger(WorkerProxyLogExporter.RelayCategory);
        using (proxy.BeginScope(new WorkerSystemLogContext
        {
            WorkerId = "worker-id",
            ProxySessionId = 7
        }))
        {
            using (proxy.BeginScope(new WorkerSystemLogContext
            {
                Operation = "RpcInvocation",
                Stage = "ResponseReceived",
                InvocationId = "invocation-id",
                Attributes = new() { RpcStatus = "Failure" }
            }))
            {
                proxy.LogDebug(new EventId(8, "ProtocolMessageObserved"), "Response received.");
            }
        }

        proxy.LogInformation(new EventId(1, "Unscoped"), "No invocation.");
        await generator.CompleteAsync();

        JsonElement[] records = ParseStructuredOutput(output);
        Assert.Equal(2, records.Length);
        Assert.Equal(2, records[0].GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("WorkerProxy", records[0].GetProperty("OriginComponent").GetString());
        Assert.Equal("worker-id", records[0].GetProperty("WorkerId").GetString());
        Assert.Equal(7, records[0].GetProperty("ProxySessionId").GetInt64());
        Assert.Equal("invocation-id", records[0].GetProperty("InvocationId").GetString());
        Assert.Equal("Failure", records[0].GetProperty("Attributes").GetProperty("RpcStatus").GetString());
        Assert.Equal("WorkerProxy", records[1].GetProperty("OriginComponent").GetString());
        Assert.Equal("WorkerProxy", records[1].GetProperty("EmitterComponent").GetString());
        Assert.False(records[1].TryGetProperty("Operation", out _));
        Assert.False(records[1].TryGetProperty("InvocationId", out _));
        Assert.False(records[1].TryGetProperty("WorkerId", out _));
        Assert.Equal(new long[] { 1, 2 }, records.Select(record => record.GetProperty("Sequence").GetInt64()));
        Assert.Single(records.Select(record => record.GetProperty("EmitterInstanceId").GetString()).Distinct());
    }

    [Fact]
    public async Task StructuredLogging_EscapesNewlinesAndOmitsExceptionPayloadAndArbitraryState()
    {
        const string sentinel = "customer-payload-sentinel";
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System, useStructuredSystemLogs: true);
        using ILoggerFactory provider = CreateLoggerFactory(generator);
        ILogger logger = provider.CreateLogger("Proxy");
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["Authorization"] = sentinel,
            ["InvocationId"] = "invocation-id",
            ["OriginComponent"] = "LanguageWorker"
        }))
        {
            logger.LogWarning(new EventId(1, "Failure"), new InvalidOperationException(sentinel),
                "first\nsecond\rthird \"quoted\" https://worker:Secret123@example.com:443 ");
        }

        await generator.CompleteAsync();

        JsonElement record = Assert.Single(ParseStructuredOutput(output));
        Assert.Equal("WorkerProxy", record.GetProperty("OriginComponent").GetString());
        Assert.Equal("invocation-id", record.GetProperty("InvocationId").GetString());
        Assert.Contains("first\nsecond\rthird", record.GetProperty("Message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, output.ToString(), StringComparison.Ordinal);
        Assert.False(record.TryGetProperty("Details", out _));
        Assert.False(record.TryGetProperty("ExceptionMessage", out _));
        Assert.Equal(typeof(InvalidOperationException).FullName, record.GetProperty("ExceptionType").GetString());
    }

    [Fact]
    public async Task StructuredLogging_PreservesProxyDiagnosticText()
    {
        const string message = "The proxy could not resolve key=worker-ready.";
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System, useStructuredSystemLogs: true);
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.ApplicationCategory);
        logger.LogWarning(message);
        await generator.CompleteAsync();

        JsonElement record = Assert.Single(ParseStructuredOutput(output));
        Assert.Equal(message, record.GetProperty("Message").GetString());
    }

    [Fact]
    public async Task StructuredLogging_BoundsRecordsAndFingerprintsUnsafeIdentities()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System, useStructuredSystemLogs: true);
        string oversized = new('\u2603', 10_000);
        generator.LogEvent(LogLevel.Warning, oversized, oversized, oversized, oversized, oversized, oversized,
            "https://worker:Secret123@example.com", new()
            {
                WorkerId = "worker\ninjection",
                Operation = oversized,
                Stage = oversized,
                Outcome = oversized,
                ReasonCode = oversized,
                Attributes = new()
                {
                    RpcStatus = oversized,
                    ForwarderError = oversized,
                    PeerSide = oversized,
                    AssignmentState = oversized
                }
            });
        await generator.CompleteAsync();

        JsonElement record = Assert.Single(ParseStructuredOutput(output));
        Assert.True(record.GetProperty("Truncated").GetBoolean());
        Assert.False(record.TryGetProperty("WorkerId", out _));
        Assert.False(record.TryGetProperty("InvocationId", out _));
        Assert.Equal(64, record.GetProperty("WorkerIdHash").GetString()!.Length);
        Assert.Equal(64, record.GetProperty("InvocationIdHash").GetString()!.Length);
        Assert.Equal(1_024, record.GetProperty("Message").GetString()!.Length);
        Assert.InRange(Encoding.UTF8.GetByteCount(output.ToString()), 1, 32_768);
        Assert.DoesNotContain("Secret123", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("injection", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StructuredLogging_UsesCapturedOpenTelemetryMetadataDuringDeferredExport()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System, useStructuredSystemLogs: true);
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
        ActivityTraceId traceId;
        ActivitySpanId spanId;
        using (Activity original = new Activity("original").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            traceId = original.TraceId;
            spanId = original.SpanId;
            using IDisposable? scope = logger.BeginScope(new WorkerSystemLogContext
            {
                WorkerId = "worker-id",
                InvocationId = "invocation-id"
            });
            logger.LogInformation(new EventId(8, "Deferred"), "Observed {Operation}.", "forwarding");
        }

        DateTimeOffset after = DateTimeOffset.UtcNow;
        using Activity unrelated = new Activity("export").SetIdFormat(ActivityIdFormat.W3C).Start();
        logger.LogInformation("Unscoped.");
        Assert.True(processor.ForceFlush(5_000));
        await generator.CompleteAsync();

        JsonElement[] records = ParseStructuredOutput(output);
        Assert.Equal(2, records.Length);
        JsonElement record = records[0];
        Assert.Equal(traceId.ToString(), record.GetProperty("TraceId").GetString());
        Assert.Equal(spanId.ToString(), record.GetProperty("SpanId").GetString());
        Assert.InRange(record.GetProperty("EventTimestamp").GetDateTimeOffset(), before, after);
        Assert.Equal("worker-id", record.GetProperty("WorkerId").GetString());
        Assert.Equal("invocation-id", record.GetProperty("InvocationId").GetString());
        Assert.Equal("Deferred", record.GetProperty("EventName").GetString());
        Assert.Equal("Observed forwarding.", record.GetProperty("Message").GetString());

        Assert.Equal("Unscoped.", records[1].GetProperty("Message").GetString());
        Assert.False(records[1].TryGetProperty("WorkerId", out _));
        Assert.False(records[1].TryGetProperty("InvocationId", out _));
    }

    [Fact]
    public async Task LogExporter_UnsupportedForceFlushDoesNotStopLogging()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System, useStructuredSystemLogs: true);
        using WorkerProxyLogExporter exporter = new(generator);
        using ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter))));
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.ApplicationCategory);
        logger.LogInformation("Before flush.");

        Assert.False(exporter.ForceFlush(5_000));
        logger.LogInformation("After flush.");
        await generator.CompleteAsync();

        Assert.Equal(["Before flush.", "After flush."],
            ParseStructuredOutput(output).Select(record => record.GetProperty("Message").GetString()));
    }

    [Fact]
    public async Task StructuredLogging_ApplicationDisposalDrainsBufferedRecords()
    {
        StringWriter output = new();
        await using (WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
        {
            services.RemoveAll<IWorkerSystemEventGenerator>();
            services.AddSingleton<IWorkerSystemEventGenerator>(_ =>
                new WorkerPodEventGenerator(output, TimeProvider.System, useStructuredSystemLogs: true));
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

        JsonElement[] records = ParseStructuredOutput(output);
        Assert.Equal(100, records.Count(record =>
            string.Equals(record.GetProperty("EventName").GetString(), "Buffered", StringComparison.Ordinal)));
        Assert.Contains(records, record =>
            string.Equals(record.GetProperty("EventName").GetString(), "WorkerProxyStopped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StructuredLogging_UsesUniqueSequencesForConcurrentEmissions()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System, useStructuredSystemLogs: true);
        await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            generator.LogEvent(LogLevel.Information, "Concurrent", "Proxy", string.Empty, "Message",
                string.Empty, string.Empty, string.Empty))));
        await generator.CompleteAsync();

        JsonElement[] records = ParseStructuredOutput(output);
        Assert.Equal(100, records.Length);
        Assert.Equal(Enumerable.Range(1, 100).Select(value => (long)value),
            records.Select(record => record.GetProperty("Sequence").GetInt64()).Order());
    }

    private static JsonElement[] ParseStructuredOutput(StringWriter output)
    {
        return output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                string prefix = $"{WorkerPodEventGenerator.StructuredEventStreamName} ";
                Assert.StartsWith(prefix, line, StringComparison.Ordinal);
                using JsonDocument document = JsonDocument.Parse(line[prefix.Length..]);
                return document.RootElement.Clone();
            }).ToArray();
    }
}
