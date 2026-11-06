// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        WorkerSystemLogContext context = new() { WorkerId = "worker-id", ProxySessionId = 7 };
        using IDisposable? scope = logger.BeginScope(context);
        logger.LogWarning(new EventId(3, "RelayFailure"), "Relay failed.");

        CapturedProxyLog record = Assert.Single(generator.ProxyRecords);
        Assert.Equal("RelayFailure", record.EventName);
        Assert.Same(context, record.Context);
        Assert.Equal("worker-id", record.Context!.WorkerId);
        Assert.Equal(7, record.Context.ProxySessionId);
        Assert.Equal(WorkerProxyLogExporter.RelayCategory, record.Source);
    }

    [Fact]
    public void LogExporter_DoesNotCreateContextWithoutTypedScopes()
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.RelayCategory);
        logger.LogInformation("No scope.");
        using (logger.BeginScope(new Dictionary<string, object?> { ["WorkerId"] = "not-a-typed-scope" }))
        {
            logger.LogInformation("Untyped scope.");
        }

        Assert.Equal(2, generator.ProxyRecords.Count);
        Assert.All(generator.ProxyRecords, record => Assert.Null(record.Context));
    }

    [Fact]
    public void LogExporter_MergesNestedScopesWithoutChangingOuterContext()
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.RelayCategory);
        WorkerSystemLogContext outer = new() { WorkerId = "outer-worker", ProxySessionId = 7 };
        WorkerSystemLogContext inner = new() { WorkerId = "inner-worker" };
        using (logger.BeginScope(outer))
        {
            using (logger.BeginScope(inner))
            {
                logger.LogInformation("Nested scopes.");
            }

            logger.LogInformation("Outer scope.");
        }

        CapturedProxyLog[] records = generator.ProxyRecords.ToArray();
        Assert.Equal(2, records.Length);
        Assert.Equal(outer with { WorkerId = inner.WorkerId }, records[0].Context);
        Assert.Same(outer, records[1].Context);
        Assert.Equal("outer-worker", outer.WorkerId);
        Assert.Equal(7, outer.ProxySessionId);
    }

    [Fact]
    public void LogExporter_NewSnapshotDoesNotInheritAnOlderSessionsIdentity()
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.RelayCategory);
        WorkerSystemLogContext outer = WorkerSystemLogContext.FromState(
            WorkerPodState.CreateUnassigned("pod", Timestamp) with { FunctionAppName = "old-app", WorkerId = "old-worker", SessionId = 1 });
        WorkerSystemLogContext inner = WorkerSystemLogContext.FromState(WorkerPodState.CreateUnassigned("pod", Timestamp));
        using IDisposable? outerScope = logger.BeginScope(outer);
        using IDisposable? innerScope = logger.BeginScope(inner);
        logger.LogInformation("New session.");

        Assert.Same(inner, Assert.Single(generator.ProxyRecords).Context);
    }

    [Fact]
    public async Task LogExporter_ConcurrentScopesRetainIndependentSnapshots()
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        ILogger logger = factory.CreateLogger(WorkerProxyLogExporter.RelayCategory);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async index =>
        {
            string identity = index.ToString(CultureInfo.InvariantCulture);
            using IDisposable? scope = logger.BeginScope(WorkerSystemLogContext.FromState(
                WorkerPodState.CreateUnassigned("pod", Timestamp) with { FunctionAppName = identity, Revision = index }));
            await Task.Yield();
            logger.LogInformation("{Identity}", identity);
        }));

        Assert.Equal(10, generator.ProxyRecords.Count);
        Assert.All(generator.ProxyRecords, record =>
        {
            Assert.Equal(record.Message, record.Context!.State!.FunctionAppName);
            Assert.Equal(record.Message, record.Context.State.Revision.ToString(CultureInfo.InvariantCulture));
        });
    }

    [Theory]
    [InlineData(LogLevel.Information, "4")]
    [InlineData(LogLevel.Warning, "3")]
    [InlineData(LogLevel.Error, "2")]
    [InlineData(LogLevel.Critical, "1")]
    public async Task PositionalLogging_AddsIdentityWithoutChangingWireContract(LogLevel level, string wireLevel)
    {
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        WorkerSystemLogContext context = WorkerSystemLogContext.FromState(
            WorkerPodState.CreateUnassigned("pod-1", Timestamp) with
            {
                WorkerId = "worker-1", SessionId = 7, FunctionAppName = "sample-app", FunctionGroupName = "http", Revision = 3
            });
        generator.LogEvent(level, "Event", "Proxy", "detail,\"text\"\r\nnext", "Human-readable, \"summary\".",
            string.Empty, string.Empty, context, Timestamp);
        await generator.CompleteAsync();

        string[] fields = Assert.Single(ParseCsvOutput(output.ToString()));
        Assert.Equal(21, fields.Length);
        Assert.Equal(wireLevel, fields[0]);
        Assert.Equal("detail,'text' next", fields[6]);
        Assert.Equal("WorkerId=worker-1; ProxySessionId=7; PodName=pod-1; FunctionAppName=sample-app; FunctionGroupName=http; Revision=3; Human-readable, 'summary'.", fields[7]);
        Assert.All(new[] { 1, 2, 3, 8, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, index => Assert.Empty(fields[index]));
    }

    [Theory]
    [InlineData("unsafe,identity\";\r\n\uD83D\uDE80", true)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public async Task PositionalLogging_BoundsAndNormalizesIdentity(string? identity, bool hashed)
    {
        identity ??= new string('x', hashed ? 129 : 128);
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        WorkerSystemLogContext context = WorkerSystemLogContext.FromState(
            WorkerPodState.CreateUnassigned(identity, Timestamp) with
            {
                WorkerId = identity, SessionId = long.MaxValue, FunctionAppName = identity, FunctionGroupName = identity, Revision = long.MaxValue
            });
        generator.LogEvent(LogLevel.Information, "Event", "Proxy", string.Empty,
            "Readable summary. " + new string('x', 2_000), string.Empty, string.Empty, context);
        await generator.CompleteAsync();

        string[] fields = Assert.Single(ParseCsvOutput(output.ToString()));
        Assert.Equal(21, fields.Length);
        Assert.Equal(1_024, fields[7].Length);
        Assert.Contains("Revision=9223372036854775807; Readable summary.", fields[7], StringComparison.Ordinal);
        foreach (string field in new[] { "WorkerId", "PodName", "FunctionAppName", "FunctionGroupName" })
        {
            Assert.Matches($@"{field}={(hashed ? "sha256:[a-f0-9]{64}" : identity)}; ", fields[7]);
        }

        Assert.NotEmpty(new UTF8Encoding(false, true).GetBytes(output.ToString()));
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
            string.Empty);

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
        Assert.Equal(string.Empty, fields[12]);
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
                string.Empty));

        Assert.Contains("\"first 'value' second value\"", formatted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void Format_PreservesExactColumnLayoutAndInvariantTimestamp(string culture)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using WorkerPodEventGenerator generator = CreateEventGenerator();
            WorkerPodEventGenerator.WorkerSystemLogRecord record = new(
                3, "Event,name", "Proxy,category", "first\r\nsecond", "Summary \"quoted\", value",
                new DateTimeOffset(2026, 11, 2, 8, 30, 0, TimeSpan.FromHours(-7)),
                "Example.Exception", "failure,\nmessage");

            string formatted = generator.Format(record);

            Assert.Equal(
                """MS_FUNCTIONS_WORKER_POD_LOGS 3,,,,"Event,name","Proxy,category","first second","Summary 'quoted', value",,2026-11-02T15:30:00.0000000Z,Example.Exception,"failure, message",,,,,,,,,""",
                formatted);
            Assert.Equal(
                new[]
                {
                    "3", string.Empty, string.Empty, string.Empty,
                    "Event,name", "Proxy,category", "first second", "Summary 'quoted', value",
                    string.Empty, "2026-11-02T15:30:00.0000000Z", "Example.Exception", "failure, message",
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty
                },
                Assert.Single(ParseCsvOutput(formatted)));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
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
            value);
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

    [Fact]
    public void Format_TruncationDoesNotSplitSurrogatePairs()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        foreach (int offset in new[] { -2, -1, 0 })
        {
            string Value(int limit) => new string('x', limit + offset) + "\uD83D\uDE80tail";
            string Expected(int limit) => offset == -2
                ? new string('x', limit - 2) + "\uD83D\uDE80"
                : new string('x', limit + Math.Min(offset, 0));
            WorkerPodEventGenerator.WorkerSystemLogRecord record = new(
                3, Value(128), Value(256), Value(1_024), Value(1_024), Timestamp, Value(256), Value(1_024));

            string formatted = generator.Format(record);

            string[] fields = Assert.Single(ParseCsvOutput(formatted));
            Assert.Equal(21, fields.Length);
            Assert.Equal(Expected(128), fields[4]);
            Assert.Equal(Expected(256), fields[5]);
            Assert.Equal(Expected(1_024), fields[6]);
            Assert.Equal(Expected(1_024), fields[7]);
            Assert.Equal(Expected(256), fields[10]);
            Assert.Equal(Expected(1_024), fields[11]);
            Assert.NotEmpty(new UTF8Encoding(false, true).GetBytes(formatted));
        }
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

    [Fact]
    public void StartupFailure_EmitsAndDrainsSafeRecordWithoutApplicationServices()
    {
        StringWriter output = new();

        WorkerProxyApplication.LogStartupFailure(new InvalidOperationException("private-configuration-sentinel"), output);

        Assert.DoesNotContain("private-configuration-sentinel", output.ToString(), StringComparison.Ordinal);
        string[] fields = Assert.Single(ParseCsvOutput(output.ToString()));
        Assert.Equal(21, fields.Length);
        Assert.Equal("WorkerProxyStartupFailed", fields[4]);
        Assert.Equal($"WorkerProxy failed to start ({typeof(InvalidOperationException).FullName}).", fields[7]);
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
        Assert.Matches(
            @"^Worker assignment request completed with HTTP status 400 in \d+(?:\.\d+)? ms\. Assignment state: Unassigned\. Worker attached and identified: False\.$",
            record.Message);
    }

    [Fact]
    public async Task LogExporter_LogsSuccessfulAssignmentDetailsWithWorkerIdentity()
    {
        CapturingEventGenerator eventGenerator = new();
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
        {
            services.RemoveAll<IWorkerSystemEventGenerator>();
            services.AddSingleton<IWorkerSystemEventGenerator>(eventGenerator);
        });
        using HttpClient client = factory.CreateWorkerProxyClient();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.True(manager.OnWorkerAttached(7));
        Assert.True(manager.OnWorkerStartStream(7, "worker-id"));
        using StringContent content = new(
            """
            {"startupMode":"Preconfigured","functionAppName":"test-app","functionGroupName":"test-group","isAlwaysReady":false,
             "environment":{},"functionAppDirectory":""}
            """,
            Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PutAsync("/admin/worker/assignment", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        CapturedProxyLog record = Assert.Single(eventGenerator.ProxyRecords, record =>
            string.Equals(record.EventName, "WorkerAssignmentRequestCompleted", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(WorkerSystemLogContext.FromState(manager.State), record.Context);
        Assert.Matches(
            @"^Worker assignment request completed with HTTP status 201 in \d+(?:\.\d+)? ms\. Assignment state: Ready\. Worker attached and identified: True\.$",
            record.Message);
        Assert.DoesNotContain("test-app", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-group", record.Message, StringComparison.Ordinal);
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
            using HttpResponseMessage response = await client.PostAsync("/admin/infra/instanceState", content: null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(
                eventGenerator.ProxyRecords,
                record => record.EventName == "WorkerProxyStarted");

            await StopApplicationAsync(factory, eventGenerator);
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
        Assert.All(eventGenerator.ProxyRecords.Where(record =>
            record.EventName is "WorkerProxyStarted" or "WorkerProxyShutdownStarted" or "WorkerProxyStopped"),
            record => Assert.NotEmpty(record.Context!.State!.PodName));
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

    private static async Task StopApplicationAsync(WorkerProxyWebApplicationFactory factory, CapturingEventGenerator eventGenerator)
    {
        // Let the entry point own graceful shutdown before factory disposal can stop and dispose the host again.
        factory.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
        await eventGenerator.Stopped.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class CapturingEventGenerator(WorkerPodEventGenerator? inner = null) : IWorkerSystemEventGenerator, IDisposable
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<CapturedProxyLog> ProxyRecords { get; } = [];

        public Task Stopped => _stopped.Task;

        public void LogEvent(
            LogLevel level,
            string eventName,
            string source,
            string details,
            string summary,
            string exceptionType,
            string exceptionMessage,
            WorkerSystemLogContext? context = null,
            DateTimeOffset? timestamp = null)
        {
            ProxyRecords.Enqueue(new(
                level,
                eventName,
                source,
                details,
                summary,
                exceptionType,
                exceptionMessage,
                context));
            inner?.LogEvent(level, eventName, source, details, summary, exceptionType, exceptionMessage, context, timestamp);
            if (string.Equals(eventName, "WorkerProxyStopped", StringComparison.Ordinal))
            {
                _stopped.TrySetResult();
            }
        }

        public void Dispose()
        {
            inner?.Dispose();
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
        WorkerSystemLogContext? Context);
}
