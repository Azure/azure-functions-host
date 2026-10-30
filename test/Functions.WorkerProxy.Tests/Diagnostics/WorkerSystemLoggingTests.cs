// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Diagnostics;

public partial class WorkerSystemLoggingTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 10, 26, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public void LogExporter_PreservesProxyExceptionContent()
    {
        const string value = "The proxy could not resolve key=worker-ready.";
        InvalidOperationException exception = new(value);
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.ApplicationCategory);
        logger.LogError(exception, "{Message}", value);

        CapturedProxyLog record = Assert.Single(generator.ProxyRecords);
        Assert.Equal(value, record.Message);
        Assert.Equal(exception.ToString(), record.Details);
        Assert.Equal(value, record.ExceptionMessage);
        Assert.Equal(typeof(InvalidOperationException).FullName, record.ExceptionType);
    }

    [Fact]
    public void LogExporter_PreservesCorrelationFields()
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.RelayCategory);
        using IDisposable? scope = logger.BeginScope(new WorkerSystemLogContext { InvocationId = "invocation-id" });
        logger.LogWarning(new EventId(3, "RelayFailure"), "Relay failed.");

        CapturedProxyLog record = Assert.Single(generator.ProxyRecords);
        Assert.Equal("RelayFailure", record.EventName);
        Assert.Equal("invocation-id", record.InvocationId);
        Assert.Equal(WorkerProxyLogExporter.RelayCategory, record.Source);
    }

    [Fact]
    public void Format_UsesSharedWorkerPodConsoleContract()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        WorkerPodEventGenerator.WorkerSystemLogRecord record = new(
            4,
            "WorkerProxyStarted",
            WorkerProxyLogExporter.ApplicationCategory,
            string.Empty,
            "WorkerProxy started.",
            Timestamp,
            string.Empty,
            string.Empty,
            "invocation-id");

        string formatted = generator.Format(record);

        Assert.StartsWith(
            $"{WorkerPodEventGenerator.EventStreamName} ",
            formatted,
            StringComparison.Ordinal);
        Assert.Contains(
            $",WorkerProxyStarted,{WorkerProxyLogExporter.ApplicationCategory},",
            formatted,
            StringComparison.Ordinal);
        Assert.Contains("\"WorkerProxy started.\"", formatted, StringComparison.Ordinal);
        Assert.Contains(Timestamp.UtcDateTime.ToString("O"), formatted, StringComparison.Ordinal);
        string[] fields = Assert.Single(ParseCsvOutput(formatted));
        Assert.Equal(21, fields.Length);
        Assert.Equal("invocation-id", fields[12]);
    }

    [Fact]
    public void Format_UsesHostStringNormalization()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();

        string formatted = generator.Format(
            new WorkerPodEventGenerator.WorkerSystemLogRecord(
                3,
                "TestEvent",
                WorkerProxyLogExporter.ApplicationCategory,
                string.Empty,
                "first \"value\"\r\nsecond value",
                Timestamp,
                string.Empty,
                string.Empty,
                string.Empty));

        Assert.Contains("\"first 'value' second value\"", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_BoundsEveryDiagnosticField()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        string value = string.Concat(new string('\u2603', 5_000), "customer-secret-sentinel");

        WorkerPodEventGenerator.WorkerSystemLogRecord record = new(
            3,
            value,
            value,
            value,
            value,
            Timestamp,
            value,
            value,
            string.Empty);
        string formatted = generator.Format(record);

        string[] fields = Assert.Single(ParseCsvOutput(formatted));
        Assert.Equal(21, fields.Length);
        Assert.Equal(128, fields[4].Length);
        Assert.Equal(256, fields[5].Length);
        Assert.Equal(1_024, fields[6].Length);
        Assert.Equal(1_024, fields[7].Length);
        Assert.Equal(256, fields[10].Length);
        Assert.Equal(1_024, fields[11].Length);
        Assert.DoesNotContain("customer-secret-sentinel", formatted, StringComparison.Ordinal);
        Assert.InRange(Encoding.UTF8.GetByteCount(formatted), 1, 32_768);
    }

    [Theory]
    [InlineData("value,extra")]
    [InlineData("value\nextra")]
    [InlineData("value\r\nextra")]
    [InlineData("value\"extra")]
    [InlineData("oversized")]
    public async Task PositionalLogging_FingerprintsUnsafeInvocationIds(string invocationId)
    {
        if (string.Equals(invocationId, "oversized", StringComparison.Ordinal))
        {
            invocationId = new string('x', 4_096);
        }

        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.HttpCategory);
        using (logger.BeginScope(new WorkerSystemLogContext { InvocationId = invocationId }))
        {
            logger.LogWarning(new EventId(1, "HttpForwardingCompleted"), "Forwarding rejected.");
        }

        await generator.CompleteAsync();

        string[] fields = Assert.Single(ParseCsvOutput(output.ToString()));
        Assert.Equal(21, fields.Length);
        Assert.Equal($"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(invocationId)))}", fields[12]);
        Assert.DoesNotContain(invocationId, output.ToString(), StringComparison.Ordinal);
        Assert.Single(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task PositionalLogging_PreservesDiagnosticTextAndEncodesColumns()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger("Proxy,category");
        logger.LogWarning(new EventId(1, "Event,name"), "first\nsecond\rthird \"quoted\" key=worker-ready");
        await generator.CompleteAsync();

        string[] fields = Assert.Single(ParseCsvOutput(output.ToString()));
        Assert.Equal(21, fields.Length);
        Assert.Equal("Event,name", fields[4]);
        Assert.Equal("Proxy,category", fields[5]);
        Assert.Equal("first second third 'quoted' key=worker-ready", fields[7]);
        Assert.Single(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData("invalid")]
    public void StartupFailure_EmitsAndFlushesSafeRecordWithoutApplicationServices(string? format)
    {
        IConfiguration? configuration = format is null ? null : new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { new KeyValuePair<string, string?>("WorkerProxy:UseStructuredSystemLogs", format) })
            .Build();
        StringWriter output = new();

        WorkerProxyApplication.LogStartupFailure(configuration, new InvalidOperationException("private-configuration-sentinel"), output);

        Assert.DoesNotContain("private-configuration-sentinel", output.ToString(), StringComparison.Ordinal);
        if (string.Equals(format, "true", StringComparison.Ordinal))
        {
            var record = Assert.Single(ParseStructuredOutput(output));
            Assert.Equal("WorkerProxyStartupFailed", record.GetProperty("EventName").GetString());
            Assert.Equal("Startup", record.GetProperty("Operation").GetString());
            Assert.Equal("Failed", record.GetProperty("Outcome").GetString());
        }
        else
        {
            string[] fields = Assert.Single(ParseCsvOutput(output.ToString()));
            Assert.Equal(21, fields.Length);
            Assert.Equal("WorkerProxyStartupFailed", fields[4]);
        }
    }

    [Fact]
    public async Task PlatformTelemetry_ExportsProxyRecordsThroughBackgroundConsolePipeline()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator eventGenerator = new(output, TimeProvider.System);
        using ILoggerFactory provider = CreateLoggerFactory(eventGenerator);
        ILogger proxyLogger = provider.CreateLogger(WorkerProxyLogExporter.ApplicationCategory);

        proxyLogger.LogInformation(new EventId(1, "WorkerProxyStarted"), "WorkerProxy started.");
        await eventGenerator.CompleteAsync();

        string[] lines = output.ToString().Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.All(
            lines,
            line => Assert.StartsWith(
                $"{WorkerPodEventGenerator.EventStreamName} ",
                line,
                StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("WorkerProxyStarted", StringComparison.Ordinal));
        Assert.Contains(
            lines,
            line => line.Contains(
                WorkerProxyLogExporter.ApplicationCategory,
                StringComparison.Ordinal));
    }

    [Fact]
    public void LogExporter_PassesReadinessDestinationWithHostExceptionDetails()
    {
        const string sentinel = "customer-secret-sentinel";
        Uri destination = new("http://worker.internal:4321/functions");
        CapturingEventGenerator eventGenerator = new();
        using ILoggerFactory provider = CreateLoggerFactory(eventGenerator);
        ILogger logger = provider.CreateLogger(
            typeof(WorkerEndpointReadinessProbe).FullName!);
        SocketException exception = new((int)SocketError.ConnectionRefused);

        logger.LogWarning(
            new EventId(2, "DestinationNotReady"),
            exception,
            "Worker HTTP destination {Destination} was not ready after {ElapsedMilliseconds} ms and {Attempts} attempts.",
            destination,
            100,
            4);

        CapturedProxyLog record = Assert.Single(eventGenerator.ProxyRecords);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("DestinationNotReady", record.EventName);
        Assert.Equal(
            $"Worker HTTP destination {destination} was not ready after 100 ms and 4 attempts.",
            record.Message);
        Assert.Equal(typeof(SocketException).FullName, record.ExceptionType);
        Assert.Contains(exception.Message, record.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LogExporter_ConsumesMissingFunctionGroupWarning()
    {
        CapturingEventGenerator eventGenerator = new();
        using ILoggerFactory provider = CreateLoggerFactory(eventGenerator);
        ILogger relayLogger = provider.CreateLogger(typeof(FunctionRpcRelay).FullName!);

        relayLogger.LogWarning(
            new EventId(3, "FunctionGroupNotAdvertised"),
            "FunctionRpc relay session {SessionId} omitted FunctionGroupName for assignment state {AssignmentState}.",
            42L,
            WorkerAssignmentState.Failed);

        CapturedProxyLog record = Assert.Single(eventGenerator.ProxyRecords);
        Assert.Equal("FunctionGroupNotAdvertised", record.EventName);
        Assert.Equal(
            "FunctionRpc relay session 42 omitted FunctionGroupName for assignment state Failed.",
            record.Message);
    }

    [Theory]
    [InlineData("/admin/worker/assignment")]
    [InlineData("/ADMIN/WORKER/ASSIGNMENT")]
    [InlineData("/admin/worker/assignment/")]
    [InlineData("/ADMIN/WORKER/ASSIGNMENT/")]
    public async Task LogExporter_ConsumesActualAspNetCoreAssignmentRequestLog(string path)
    {
        CapturingEventGenerator eventGenerator = new();
        await using global::Azure.Functions.WorkerProxy.Tests.WorkerProxyWebApplicationFactory factory =
            new(configureServices: services =>
            {
                services.RemoveAll<IWorkerSystemEventGenerator>();
                services.AddSingleton<IWorkerSystemEventGenerator>(eventGenerator);
            });
        using HttpClient client = factory.CreateWorkerProxyClient();
        using StringContent content = new("{}", Encoding.UTF8, "application/json");

        using HttpResponseMessage response =
            await client.PutAsync(path, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        CapturedProxyLog record = Assert.Single(
            eventGenerator.ProxyRecords,
            record => string.Equals(record.EventName, "WorkerAssignmentRequestRejected", StringComparison.Ordinal));
        Assert.Equal(
            "Worker assignment request completed with HTTP status 400.",
            record.Message);
    }

    [Fact]
    public async Task LogExporter_FiltersConfiguredCategoryInformationLogs()
    {
        CapturingEventGenerator eventGenerator = new();
        await using global::Azure.Functions.WorkerProxy.Tests.WorkerProxyWebApplicationFactory factory =
            new(configureServices: services =>
            {
                services.RemoveAll<IWorkerSystemEventGenerator>();
                services.AddSingleton<IWorkerSystemEventGenerator>(eventGenerator);
            });
        using HttpClient client = factory.CreateWorkerProxyClient();
        ILoggerFactory loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();
        ILogger readinessLogger =
            loggerFactory.CreateLogger(WorkerProxyLogExporter.ReadinessProbeCategory);
        ILogger relayLogger =
            loggerFactory.CreateLogger(WorkerProxyLogExporter.RelayCategory);
        ILogger grpcLogger =
            loggerFactory.CreateLogger("Grpc.AspNetCore.Server.ServerCallHandler");
        ILogger httpLogger =
            loggerFactory.CreateLogger("Yarp.ReverseProxy.Forwarder.HttpForwarder");

        readinessLogger.LogInformation(
            new EventId(1),
            "Worker destination was ready after {Attempts} attempts.",
            1);
        readinessLogger.LogWarning(
            new EventId(2, "DestinationNotReady"),
            "Worker HTTP destination http://worker.internal:4321/functions was not ready after 100 ms and 2 attempts.");
        relayLogger.LogInformation(
            new EventId(4),
            "FunctionRpc session {SessionId} accepted the {Side} attachment.",
            42L,
            FunctionRpcRelaySide.Worker);
        relayLogger.LogWarning(
            new EventId(3, "FunctionGroupNotAdvertised"),
            "FunctionRpc relay session {SessionId} omitted FunctionGroupName for assignment state {AssignmentState}.",
            42L,
            WorkerAssignmentState.Failed);
        grpcLogger.LogInformation(new EventId(10, "GrpcInformation"), "Ignored gRPC information.");
        grpcLogger.LogWarning(new EventId(11, "GrpcWarning"), "Captured gRPC warning.");
        httpLogger.LogInformation(new EventId(20, "HttpInformation"), "Ignored HTTP information.");
        httpLogger.LogError(new EventId(21, "HttpError"), "Captured HTTP error.");

        Assert.DoesNotContain(
            eventGenerator.ProxyRecords,
            record => record.Message.Contains("Ignored", StringComparison.Ordinal));
        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => record.EventName == "DestinationNotReady"
                && record.Message == "Worker HTTP destination http://worker.internal:4321/functions was not ready after 100 ms and 2 attempts.");
        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => record.EventName == "FunctionGroupNotAdvertised");
        Assert.DoesNotContain(
            eventGenerator.ProxyRecords,
            record => record.EventName is "GrpcWarning" or "HttpError");
    }

    [Fact]
    public async Task ApplicationLifetime_EmitsStartupAndShutdownEvents()
    {
        CapturingEventGenerator eventGenerator = new();
        global::Azure.Functions.WorkerProxy.Tests.WorkerProxyWebApplicationFactory factory =
            new(configureServices: services =>
            {
                services.RemoveAll<IWorkerSystemEventGenerator>();
                services.AddSingleton<IWorkerSystemEventGenerator>(eventGenerator);
            });

        try
        {
            using HttpClient client = factory.CreateWorkerProxyClient();
            using HttpResponseMessage response = await client.GetAsync("/admin/worker/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains(
                eventGenerator.ProxyRecords,
                record => record.EventName == "WorkerProxyStarted");
        }
        finally
        {
            await factory.DisposeAsync();
        }

        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => record.EventName == "WorkerProxyShutdownStarted");
        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => string.Equals(record.EventName, "WorkerProxyStopped", StringComparison.Ordinal));
    }

    private static string[][] ParseCsvOutput(string output)
    {
        return output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            string prefix = $"{WorkerPodEventGenerator.EventStreamName} ";
            Assert.StartsWith(prefix, line, StringComparison.Ordinal);
            using TextFieldParser parser = new(new StringReader(line[prefix.Length..]));
            parser.SetDelimiters(",");
            return parser.ReadFields()!;
        }).ToArray();
    }

    private static WorkerPodEventGenerator CreateEventGenerator()
    {
        return new(new StringWriter(), TimeProvider.System);
    }

    private static ILoggerFactory CreateLoggerFactory(IWorkerSystemEventGenerator generator)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.AddProcessor(new SimpleLogRecordExportProcessor(new WorkerProxyLogExporter(generator)));
            });
        });
    }

    private sealed class CapturingEventGenerator : IWorkerSystemEventGenerator
    {
        public ConcurrentQueue<CapturedProxyLog> ProxyRecords { get; } = [];

        public void LogEvent(
            LogLevel level,
            string eventName,
            string source,
            string details,
            string summary,
            string exceptionType,
            string exceptionMessage,
            string invocationId,
            WorkerSystemLogContext? context = null,
            DateTimeOffset? timestamp = null,
            ActivityTraceId traceId = default,
            ActivitySpanId spanId = default)
        {
            ProxyRecords.Enqueue(new(
                level,
                eventName,
                source,
                details,
                summary,
                exceptionType,
                exceptionMessage,
                invocationId));
        }
    }

    private sealed record CapturedProxyLog(
        LogLevel Level,
        string EventName,
        string Source,
        string Details,
        string Message,
        string ExceptionType,
        string ExceptionMessage,
        string InvocationId);
}
