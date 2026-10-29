// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Diagnostics;

public class WorkerSystemLoggingTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 10, 26, 12, 34, 56, TimeSpan.Zero);

    [Theory]
    [InlineData(RpcLog.Types.Level.Trace, 5, false)]
    [InlineData(RpcLog.Types.Level.Debug, 5, false)]
    [InlineData(RpcLog.Types.Level.Information, 4, false)]
    [InlineData(RpcLog.Types.Level.Warning, 3, false)]
    [InlineData(RpcLog.Types.Level.Error, 2, true)]
    [InlineData(RpcLog.Types.Level.Critical, 1, true)]
    [InlineData(RpcLog.Types.Level.None, 0, false)]
    public void CreateWorkerRecord_MatchesHostSystemLogPayload(
        RpcLog.Types.Level rpcLevel,
        int expectedLevel,
        bool expectedException)
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        RpcLog log = CreateWorkerLog(rpcLevel);

        WorkerPodEventGenerator.WorkerSystemLogRecord record =
            generator.CreateWorkerRecord(log, Timestamp);

        Assert.Equal(expectedLevel, record.Level);
        Assert.Equal("worker-system-message", record.Summary);
        Assert.Equal(WorkerPodEventGenerator.WorkerSource, record.Source);
        Assert.Equal(Timestamp, record.Timestamp);
        if (expectedException)
        {
            const string exceptionMessage =
                "Result: worker-system-message\n"
                + "Type: \n"
                + "Exception: worker-remote-exception\n"
                + "Stack: worker-remote-stack";
            Assert.Equal(
                "Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException",
                record.ExceptionType);
            Assert.Equal(exceptionMessage, record.ExceptionMessage);
            Assert.Equal($"{record.ExceptionType}: {exceptionMessage}", record.Details);
        }
        else
        {
            Assert.Empty(record.ExceptionType);
            Assert.Empty(record.ExceptionMessage);
            Assert.Empty(record.Details);
        }
    }

    [Fact]
    public void CreateWorkerRecord_SanitizesWorkerFields()
    {
        const string secret = "SuperSecret123";
        string credential = $"https://worker:{secret}@example.com:443 ";
        string value = $"worker failure {credential}";
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Error);
        log.Message = value;
        log.Exception.Message = value;
        log.Exception.StackTrace = value;

        WorkerPodEventGenerator.WorkerSystemLogRecord record =
            generator.CreateWorkerRecord(log, Timestamp);
        string formatted = generator.Format(record);

        Assert.DoesNotContain(secret, formatted, StringComparison.Ordinal);
        Assert.Contains(Sanitizer.SecretReplacement, formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWorkerRecord_SanitizesBeforeUtf8Truncation()
    {
        const string secret = "SuperSecret123";
        string credential = $"https://worker:{secret}@example.com:443 ";
        string value = new string('a', WorkerPodEventGenerator.MaximumMessageBytes - 20)
            + credential
            + string.Concat(Enumerable.Repeat("\U0001F680", 2_000));
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Error);
        log.Message = value;
        log.Exception.Message = value;
        log.Exception.StackTrace = value;

        string formatted = generator.Format(generator.CreateWorkerRecord(log, Timestamp));

        Assert.DoesNotContain(secret, formatted, StringComparison.Ordinal);
        Assert.Contains(Sanitizer.SecretReplacement, formatted, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', formatted);
        Assert.Contains("...[truncated]", formatted, StringComparison.Ordinal);
        Assert.True(
            Encoding.UTF8.GetByteCount(formatted) <= WorkerPodEventGenerator.MaximumRecordBytes);
    }

    [Fact]
    public void CreateWorkerRecord_ExcludesUnusedWorkerFields()
    {
        const string sentinel = "customer-secret-sentinel";
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Information);
        log.Category = sentinel;
        log.Properties = sentinel;
        log.PropertiesMap["secret"] = new TypedData { String = sentinel };

        string formatted = generator.Format(generator.CreateWorkerRecord(log, Timestamp));

        Assert.DoesNotContain(sentinel, formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWorkerRecord_PreservesHostCorrelationFields()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Warning);
        log.EventId = "WorkerFailure";
        log.InvocationId = "invocation-id";

        WorkerPodEventGenerator.WorkerSystemLogRecord record =
            generator.CreateWorkerRecord(log, Timestamp);
        string formatted = generator.Format(record);

        Assert.Equal("WorkerFailure", record.EventName);
        Assert.Equal("invocation-id", record.InvocationId);
        Assert.Contains(
            ",WorkerFailure,Worker.LanguageWorker,",
            formatted,
            StringComparison.Ordinal);
        Assert.Contains(
            ",\"\",invocation-id,,,,,,,,",
            formatted,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWorkerRecord_OmitsFieldsThatExceedSanitizationWorkLimit()
    {
        string value = string.Concat(Enumerable.Repeat("key='", 65));
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Error);
        log.Message = value;
        log.Exception.Message = value;
        log.Exception.StackTrace = value;

        string formatted = generator.Format(generator.CreateWorkerRecord(log, Timestamp));

        Assert.DoesNotContain(value, formatted, StringComparison.Ordinal);
        Assert.Contains(
            "sanitization work exceeded the limit",
            formatted,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogWorkerEvent_DoesNotSanitizeOnCallingThread()
    {
        string value = string.Concat(
            Enumerable.Repeat(
                "key='",
                6_000));
        StringWriter output = new();
        using WorkerPodEventGenerator generator = new(output, TimeProvider.System);
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Error);
        log.Message = value;
        log.Exception.Message = value;
        log.Exception.StackTrace = value;

        long started = Stopwatch.GetTimestamp();
        generator.LogWorkerEvent(log);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"Worker logging took {elapsed}.");
        await generator.CompleteAsync();
        Assert.Contains(
            "sanitization work exceeded the limit",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Format_UsesSharedWorkerPodConsoleContract()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        WorkerPodEventGenerator.WorkerSystemLogRecord record = generator.CreateProxyRecord(
            LogLevel.Information,
            "WorkerProxyStarted",
            "WorkerProxy started.",
            Timestamp);

        string formatted = generator.Format(record);

        Assert.StartsWith(
            $"{WorkerPodEventGenerator.EventStreamName} ",
            formatted,
            StringComparison.Ordinal);
        Assert.Contains(",WorkerProxyStarted,Worker.Proxy,", formatted, StringComparison.Ordinal);
        Assert.Contains("\"WorkerProxy started.\"", formatted, StringComparison.Ordinal);
        Assert.Contains(Timestamp.UtcDateTime.ToString("O"), formatted, StringComparison.Ordinal);
        Assert.True(
            Encoding.UTF8.GetByteCount(formatted) <= WorkerPodEventGenerator.MaximumRecordBytes);
    }

    [Fact]
    public void Format_UsesHostStringNormalization()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();

        string formatted = generator.Format(
            generator.CreateProxyRecord(
                LogLevel.Warning,
                "TestEvent",
                "first \"value\"\r\nsecond value",
                Timestamp));

        Assert.Contains("\"first 'value' second value\"", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateProxyRecord_BoundsUtf8Fields()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        string value = string.Concat(
            new string('a', WorkerPodEventGenerator.MaximumMessageBytes),
            "\U0001F680",
            "customer-secret-sentinel");

        string formatted = generator.Format(
            generator.CreateProxyRecord(LogLevel.Warning, value, value, Timestamp));

        Assert.DoesNotContain("customer-secret-sentinel", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', formatted);
        Assert.Contains("...[truncated]", formatted, StringComparison.Ordinal);
        Assert.True(
            Encoding.UTF8.GetByteCount(formatted) <= WorkerPodEventGenerator.MaximumRecordBytes);
    }

    [Fact]
    public async Task PlatformTelemetry_WritesProxyAndWorkerRecordsThroughBackgroundConsolePipeline()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator eventGenerator = new(output, TimeProvider.System);

        eventGenerator.LogProxyEvent(
            LogLevel.Information,
            "WorkerProxyStarted",
            "WorkerProxy started.");
        RpcLog workerLog = CreateWorkerLog(RpcLog.Types.Level.Warning);
        workerLog.EventId = "WorkerFailure";
        workerLog.InvocationId = "invocation-id";
        eventGenerator.LogWorkerEvent(workerLog);
        await eventGenerator.CompleteAsync();

        string[] lines = output.ToString().Split(
            Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(
            lines,
            line => Assert.StartsWith(
                $"{WorkerPodEventGenerator.EventStreamName} ",
                line,
                StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("WorkerProxyStarted", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Worker.Proxy", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Worker.LanguageWorker", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("WorkerFailure", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("invocation-id", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("worker-system-message", StringComparison.Ordinal));
    }

    [Fact]
    public void LoggerProvider_PassesSafeReadinessLogWithoutExceptionDetails()
    {
        const string sentinel = "customer-secret-sentinel";
        CapturingEventGenerator eventGenerator = new();
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
        ILogger logger = provider.CreateLogger(
            typeof(WorkerEndpointReadinessProbe).FullName!);
        SocketException exception = new((int)SocketError.ConnectionRefused);

        logger.LogWarning(
            new EventId(2, "WorkerHttpReadinessFailed"),
            exception,
            "Worker HTTP readiness completed with {Result} after {ElapsedMilliseconds} ms and {Attempts} attempts.",
            WorkerEndpointReadinessResult.ConnectionRefused,
            100,
            4);

        CapturedProxyLog record = Assert.Single(eventGenerator.ProxyRecords);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("WorkerHttpReadinessFailed", record.EventName);
        Assert.Equal(
            "Worker HTTP readiness completed with ConnectionRefused after 100 ms and 4 attempts.",
            record.Message);
        Assert.DoesNotContain(sentinel, record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(exception.Message, record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoggerProvider_ConsumesMissingFunctionGroupWarning()
    {
        CapturingEventGenerator eventGenerator = new();
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
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

    [Fact]
    public void LoggerProvider_PassesWorkerOwnedLogWithoutTranslation()
    {
        CapturingEventGenerator eventGenerator = new();
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
        ILogger relayLogger = provider.CreateLogger(typeof(FunctionRpcRelay).FullName!);

        relayLogger.LogWarning(
            new EventId(7, "WorkerStartStreamRejected"),
            "FunctionRpc relay session {SessionId} rejected worker StartStream because InvalidMessage.",
            42L);

        CapturedProxyLog record = Assert.Single(eventGenerator.ProxyRecords);
        Assert.Equal("WorkerStartStreamRejected", record.EventName);
        Assert.Equal(
            "FunctionRpc relay session 42 rejected worker StartStream because InvalidMessage.",
            record.Message);
    }

    [Fact]
    public async Task LoggerProvider_ConsumesActualAspNetCoreAssignmentRequestLog()
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
            await client.PutAsync("/admin/worker/assignment", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        CapturedProxyLog record = Assert.Single(
            eventGenerator.ProxyRecords,
            record => record.EventName == "WorkerAssignmentRequestRejected");
        Assert.Equal(
            "Worker assignment request completed with HTTP status 400.",
            record.Message);
    }

    [Fact]
    public async Task LoggerProvider_FiltersConfiguredCategoryInformationLogs()
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
            loggerFactory.CreateLogger(WorkerSystemLoggerProvider.ReadinessProbeCategory);
        ILogger relayLogger =
            loggerFactory.CreateLogger(WorkerSystemLoggerProvider.RelayCategory);
        ILogger grpcLogger =
            loggerFactory.CreateLogger("Grpc.AspNetCore.Server.ServerCallHandler");
        ILogger httpLogger =
            loggerFactory.CreateLogger("Yarp.ReverseProxy.Forwarder.HttpForwarder");

        readinessLogger.LogInformation(
            new EventId(1),
            "Worker destination was ready after {Attempts} attempts.",
            1);
        readinessLogger.LogWarning(
            new EventId(2, "WorkerHttpReadinessFailed"),
            "Worker HTTP readiness completed with Timeout after 100 ms and 2 attempts.");
        relayLogger.LogInformation(
            new EventId(4),
            "FunctionRpc session {SessionId} accepted the {Side} attachment.",
            42L,
            FunctionRpcRelaySide.Worker);
        relayLogger.LogWarning(
            new EventId(5, "AttachmentRejected"),
            "FunctionRpc session {SessionId} rejected the {Side} attachment because {Reason}.",
            42L,
            FunctionRpcRelaySide.Worker,
            FunctionRpcRelayAttachmentFailure.Shutdown);
        grpcLogger.LogInformation(new EventId(10, "GrpcInformation"), "Ignored gRPC information.");
        grpcLogger.LogWarning(new EventId(11, "GrpcWarning"), "Captured gRPC warning.");
        httpLogger.LogInformation(new EventId(20, "HttpInformation"), "Ignored HTTP information.");
        httpLogger.LogError(new EventId(21, "HttpError"), "Captured HTTP error.");

        Assert.DoesNotContain(
            eventGenerator.ProxyRecords,
            record => record.Message.Contains("Ignored", StringComparison.Ordinal));
        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => record.EventName == "WorkerHttpReadinessFailed"
                && record.Message == "Worker HTTP readiness completed with Timeout after 100 ms and 2 attempts.");
        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => record.EventName == "AttachmentRejected");
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
    }

    private static WorkerPodEventGenerator CreateEventGenerator()
    {
        return new(new StringWriter(), TimeProvider.System);
    }

    private static RpcLog CreateWorkerLog(RpcLog.Types.Level level)
    {
        return new RpcLog
        {
            LogCategory = RpcLog.Types.RpcLogCategory.System,
            Level = level,
            Message = "worker-system-message",
            Exception = new RpcException
            {
                Message = "worker-remote-exception",
                StackTrace = "worker-remote-stack"
            }
        };
    }

    private sealed class CapturingEventGenerator : IWorkerSystemEventGenerator
    {
        public ConcurrentQueue<CapturedProxyLog> ProxyRecords { get; } = [];

        public void LogWorkerEvent(RpcLog log)
        {
        }

        public void LogProxyEvent(LogLevel level, string eventName, string message)
        {
            ProxyRecords.Enqueue(new(level, eventName, message));
        }
    }

    private sealed record CapturedProxyLog(LogLevel Level, string EventName, string Message);
}
