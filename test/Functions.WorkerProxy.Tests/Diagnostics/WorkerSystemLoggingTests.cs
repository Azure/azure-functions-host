// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
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
    [InlineData(RpcLog.Types.Level.Trace, 4, false)]
    [InlineData(RpcLog.Types.Level.Debug, 4, false)]
    [InlineData(RpcLog.Types.Level.Information, 4, false)]
    [InlineData(RpcLog.Types.Level.Warning, 3, false)]
    [InlineData(RpcLog.Types.Level.Error, 2, true)]
    [InlineData(RpcLog.Types.Level.Critical, 1, true)]
    [InlineData(RpcLog.Types.Level.None, 4, false)]
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

        WorkerPodEventGenerator.WorkerSystemLogRecord record =
            generator.CreateWorkerRecord(log, Timestamp);
        string formatted = generator.Format(record);

        Assert.DoesNotContain(secret, formatted, StringComparison.Ordinal);
        Assert.Contains(Sanitizer.SecretReplacement, formatted, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', formatted);
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
        log.EventId = sentinel;
        log.InvocationId = sentinel;
        log.Properties = sentinel;
        log.PropertiesMap["secret"] = new TypedData { String = sentinel };

        string formatted = generator.Format(generator.CreateWorkerRecord(log, Timestamp));

        Assert.DoesNotContain(sentinel, formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_UsesWorkerProxyConsoleContract()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        WorkerPodEventGenerator.WorkerSystemLogRecord record = generator.CreateProxyRecord(
            LogLevel.Information,
            "WorkerProxyStarted",
            "WorkerProxy started.",
            Timestamp);

        string formatted = generator.Format(record);

        Assert.StartsWith(
            $"{WorkerPodEventGenerator.ProxyEventStreamName} ",
            formatted,
            StringComparison.Ordinal);
        Assert.Contains(",WorkerProxyStarted,Worker.Proxy,", formatted, StringComparison.Ordinal);
        Assert.Contains("\"WorkerProxy started.\"", formatted, StringComparison.Ordinal);
        Assert.Contains(Timestamp.UtcDateTime.ToString("O"), formatted, StringComparison.Ordinal);
        Assert.True(
            Encoding.UTF8.GetByteCount(formatted) <= WorkerPodEventGenerator.MaximumRecordBytes);
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
        eventGenerator.LogWorkerEvent(CreateWorkerLog(RpcLog.Types.Level.Warning));
        await eventGenerator.CompleteAsync();

        Assert.Contains(
            $"{WorkerPodEventGenerator.ProxyEventStreamName} ",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            $"{WorkerPodEventGenerator.WorkerEventStreamName} ",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.Contains("WorkerProxyStarted", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Worker.LanguageWorker", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("worker-system-message", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writer_FullQueue_DropsWithoutBlockingCaller()
    {
        BlockingTextWriter output = new();
        using WorkerPodEventGenerator writer = new(output, TimeProvider.System);
        WorkerPodEventGenerator.WorkerSystemLogRecord record = writer.CreateProxyRecord(
            LogLevel.Information,
            "TestEvent",
            "test",
            Timestamp);

        Assert.True(writer.TryEnqueue(record));
        await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 0; i < WorkerPodEventGenerator.QueueCapacity; i++)
        {
            Assert.True(writer.TryEnqueue(record));
        }

        Assert.False(writer.TryEnqueue(record));
        Assert.Equal(1, writer.TotalDroppedRecords);

        output.Release();
        await writer.CompleteAsync();

        Assert.Contains("WorkerSystemLogsDropped", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("dropped 1 system log records", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writer_OutputFailure_DropsQueuedRecordsAndRejectsNewRecords()
    {
        FailingTextWriter output = new();
        using WorkerPodEventGenerator writer = new(output, TimeProvider.System);
        WorkerPodEventGenerator.WorkerSystemLogRecord record = writer.CreateProxyRecord(
            LogLevel.Information,
            "TestEvent",
            "test",
            Timestamp);

        Assert.True(writer.TryEnqueue(record));
        await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(writer.TryEnqueue(record));
        Assert.True(writer.TryEnqueue(record));

        output.Release();
        await writer.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, writer.TotalDroppedRecords);
        Assert.False(writer.TryEnqueue(record));
        Assert.Equal(4, writer.TotalDroppedRecords);
    }

    [Fact]
    public async Task Writer_BatchedOutputFailure_CountsEveryDequeuedRecord()
    {
        FailingBatchTextWriter output = new();
        using WorkerPodEventGenerator writer = new(output, TimeProvider.System);
        WorkerPodEventGenerator.WorkerSystemLogRecord record = writer.CreateProxyRecord(
            LogLevel.Information,
            "TestEvent",
            "test",
            Timestamp);

        Assert.True(writer.TryEnqueue(record));
        await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(writer.TryEnqueue(record));
        Assert.True(writer.TryEnqueue(record));
        Assert.True(writer.TryEnqueue(record));

        output.Release();
        await writer.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, writer.TotalDroppedRecords);
    }

    [Fact]
    public void LoggerProvider_ConsumesRelayAttachmentLog()
    {
        CapturingEventGenerator eventGenerator = new();
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
        ILogger logger = provider.CreateLogger(typeof(FunctionRpcRelay).FullName!);

        logger.LogInformation(
            new EventId(4, "AttachmentAccepted"),
            "FunctionRpc relay session {SessionId} accepted the {Side} attachment.",
            42L,
            FunctionRpcRelaySide.Worker);

        CapturedProxyLog record = Assert.Single(eventGenerator.ProxyRecords);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("RelayAttachmentAccepted", record.EventName);
        Assert.Equal(
            "FunctionRpc session 42 accepted the Worker attachment.",
            record.Message);
    }

    [Fact]
    public void LoggerProvider_ConsumesReadinessLogWithoutDestinationOrExceptionDetails()
    {
        const string sentinel = "customer-secret-sentinel";
        CapturingEventGenerator eventGenerator = new();
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
        ILogger logger = provider.CreateLogger(
            typeof(WorkerEndpointReadinessProbe).FullName!);
        SocketException exception = new((int)SocketError.ConnectionRefused);

        logger.LogWarning(
            new EventId(2, "DestinationNotReady"),
            exception,
            "Worker HTTP destination {Destination} was not ready after {ElapsedMilliseconds} ms and {Attempts} attempts.",
            new Uri($"http://{sentinel}:8080"),
            100,
            4);

        CapturedProxyLog record = Assert.Single(eventGenerator.ProxyRecords);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("WorkerHttpReadinessFailed", record.EventName);
        Assert.Equal(
            "Worker HTTP readiness completed with ConnectionRefused after 4 attempts.",
            record.Message);
        Assert.DoesNotContain(sentinel, record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(exception.Message, record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoggerProvider_ConsumesExistingRelayLogs()
    {
        CapturingEventGenerator eventGenerator = new();
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
        ILogger relayLogger = provider.CreateLogger(typeof(FunctionRpcRelay).FullName!);

        relayLogger.LogDebug(
            new EventId(1, "SessionTerminated"),
            "FunctionRpc relay session {SessionId} terminated with {Reason} on the {Side} side.",
            42L,
            FunctionRpcRelayTerminationReason.PeerClosed,
            FunctionRpcRelaySide.Worker);

        CapturedProxyLog record = Assert.Single(eventGenerator.ProxyRecords);
        Assert.Equal("RelaySessionTerminated", record.EventName);
        Assert.Equal(
            "FunctionRpc session 42 was terminated by Worker because PeerClosed.",
            record.Message);
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
        Assert.Equal("WorkerFunctionGroupNotAdvertised", record.EventName);
        Assert.Equal(
            "FunctionRpc session 42 did not advertise FunctionGroupName because the worker assignment state was Failed.",
            record.Message);
    }

    [Fact]
    public void LoggerProvider_ConsumesAspNetCoreAssignmentAndRelayLogs()
    {
        CapturingEventGenerator eventGenerator = new();
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
        ILogger aspNetCoreLogger =
            provider.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");
        ILogger relayLogger = provider.CreateLogger(typeof(FunctionRpcRelay).FullName!);

        aspNetCoreLogger.LogInformation(
            new EventId(2, "RequestFinished"),
            "Request finished {Method} {Path} - {StatusCode}",
            "PUT",
            "/admin/worker/assignment",
            StatusCodes.Status409Conflict);
        aspNetCoreLogger.LogInformation(
            new EventId(2, "RequestFinished"),
            "Request finished {Method} {Path} - {StatusCode}",
            "GET",
            "/admin/worker/assignment",
            StatusCodes.Status409Conflict);
        aspNetCoreLogger.LogInformation(
            new EventId(2, "RequestFinished"),
            "Request finished {Method} {Path} - {StatusCode}",
            "PUT",
            "/admin/worker/assignment",
            StatusCodes.Status404NotFound);
        relayLogger.LogWarning(
            new EventId(7, "WorkerStartStreamRejected"),
            "FunctionRpc relay session {SessionId} rejected worker StartStream because {Reason}.",
            42L,
            WorkerStartStreamFailure.StaleOrRepeated);
        relayLogger.LogInformation(
            new EventId(8, "WorkerInitializationCompleted"),
            "FunctionRpc relay session {SessionId} completed worker initialization.",
            42L);

        CapturedProxyLog[] records = eventGenerator.ProxyRecords.ToArray();
        Assert.Collection(
            records,
            record =>
            {
                Assert.Equal("WorkerAssignmentRequestRejected", record.EventName);
                Assert.Equal(
                    "Worker assignment request completed with HTTP status 409.",
                    record.Message);
            },
            record =>
            {
                Assert.Equal("WorkerStartStreamRejected", record.EventName);
                Assert.Equal(
                    "FunctionRpc session 42 rejected worker StartStream because StaleOrRepeated.",
                    record.Message);
            },
            record =>
            {
                Assert.Equal("WorkerInitializationCompleted", record.EventName);
                Assert.Equal(
                    "FunctionRpc session 42 completed worker initialization.",
                    record.Message);
            });
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
            new EventId(2),
            "Worker destination was not ready after {Attempts} attempts.",
            2);
        relayLogger.LogInformation(
            new EventId(4),
            "FunctionRpc session {SessionId} accepted the {Side} attachment.",
            42L,
            FunctionRpcRelaySide.Worker);
        relayLogger.LogWarning(
            new EventId(5),
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
            record => record.EventName is "WorkerHttpReadinessCompleted" or "RelayAttachmentAccepted"
                || record.Message.Contains("Ignored", StringComparison.Ordinal));
        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => record.EventName == "WorkerHttpReadinessFailed");
        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => record.EventName == "RelayAttachmentRejected");
        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => record.EventName == "GrpcFrameworkLog"
                && record.Message == "Grpc.AspNetCore.Server.ServerCallHandler [11:GrpcWarning] Captured gRPC warning.");
        Assert.Contains(
            eventGenerator.ProxyRecords,
            record => record.EventName == "HttpFrameworkLog"
                && record.Message == "Yarp.ReverseProxy.Forwarder.HttpForwarder [21:HttpError] Captured HTTP error.");
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

    private sealed class BlockingTextWriter : StringWriter
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task WriteLineAsync(string? value)
        {
            WriteStarted.TrySetResult();
            await _release.Task;
            await base.WriteLineAsync(value);
        }

        public override async Task WriteAsync(string? value)
        {
            WriteStarted.TrySetResult();
            await _release.Task;
            await base.WriteAsync(value);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class FailingTextWriter : StringWriter
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task WriteLineAsync(string? value)
        {
            WriteStarted.TrySetResult();
            await _release.Task;
            throw new IOException("Console output failed.");
        }

        public override async Task WriteAsync(string? value)
        {
            WriteStarted.TrySetResult();
            await _release.Task;
            throw new IOException("Console output failed.");
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class FailingBatchTextWriter : StringWriter
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource WriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task WriteLineAsync(string? value)
        {
            WriteStarted.TrySetResult();
            await _release.Task;
            await base.WriteLineAsync(value);
        }

        public override Task WriteAsync(string? value)
        {
            throw new IOException("Batched console output failed.");
        }

        public void Release() => _release.TrySetResult();
    }
}
