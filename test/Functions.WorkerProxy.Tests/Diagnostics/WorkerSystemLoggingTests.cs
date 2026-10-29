// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.IO;
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
    [InlineData(RpcLog.Types.Level.Information, LogLevel.Information, false)]
    [InlineData(RpcLog.Types.Level.Warning, LogLevel.Warning, false)]
    [InlineData(RpcLog.Types.Level.Error, LogLevel.Error, true)]
    [InlineData(RpcLog.Types.Level.Critical, LogLevel.Critical, true)]
    public void WorkerLogger_MatchesHostSystemLogPayload(
        RpcLog.Types.Level rpcLevel,
        LogLevel expectedLevel,
        bool expectedException)
    {
        RpcLog log = CreateWorkerLog(rpcLevel);

        CapturedProxyLog record = CaptureWorkerRecord(log);

        Assert.Equal(expectedLevel, record.Level);
        Assert.Equal("worker-system-message", record.Message);
        Assert.Equal(WorkerPodEventGenerator.WorkerSource, record.Source);
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
            Assert.Contains($"{record.ExceptionType}: {exceptionMessage}", record.Details, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(record.ExceptionType);
            Assert.Empty(record.ExceptionMessage);
            Assert.Empty(record.Details);
        }
    }

    [Fact]
    public void WorkerLogger_SanitizesExceptionContent()
    {
        const string secret = "SuperSecret123";
        string credential = $"https://worker:{secret}@example.com:443 ";
        string value = $"worker failure {credential}";
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Error);
        log.Message = value;
        log.Exception.Message = value;
        log.Exception.StackTrace = value;

        CapturedProxyLog record = CaptureWorkerRecord(log);
        string formatted = string.Join(
            ',',
            record.Message,
            record.Details,
            record.ExceptionMessage);

        Assert.DoesNotContain(secret, formatted, StringComparison.Ordinal);
        Assert.Contains(Sanitizer.SecretReplacement, record.Message, StringComparison.Ordinal);
        Assert.Contains(Sanitizer.SecretReplacement, record.Details, StringComparison.Ordinal);
        Assert.Contains(Sanitizer.SecretReplacement, record.ExceptionMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerLogger_DoesNotApplyCustomFieldLimits()
    {
        string summary = new('a', 5_000);
        string remoteMessage = new('b', 3_000);
        string remoteStack = new('c', 7_000);
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Error);
        log.Message = summary;
        log.Exception.Message = remoteMessage;
        log.Exception.StackTrace = remoteStack;

        CapturedProxyLog record = CaptureWorkerRecord(log);

        Assert.Equal(summary, record.Message);
        Assert.Contains(remoteMessage, record.ExceptionMessage, StringComparison.Ordinal);
        Assert.Contains(remoteStack, record.ExceptionMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerLogger_ExcludesUnusedWorkerFields()
    {
        const string sentinel = "customer-secret-sentinel";
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Information);
        log.Category = sentinel;
        log.Properties = sentinel;
        log.PropertiesMap["secret"] = new TypedData { String = sentinel };

        CapturedProxyLog record = CaptureWorkerRecord(log);
        string formatted = string.Join(
            ',',
            record.EventName,
            record.Source,
            record.Details,
            record.Message,
            record.ExceptionType,
            record.ExceptionMessage,
            record.InvocationId);

        Assert.DoesNotContain(sentinel, formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerLogger_PreservesHostCorrelationFields()
    {
        RpcLog log = CreateWorkerLog(RpcLog.Types.Level.Warning);
        log.EventId = "WorkerFailure";
        log.InvocationId = "invocation-id";

        CapturedProxyLog record = CaptureWorkerRecord(log);

        Assert.Equal("WorkerFailure", record.EventName);
        Assert.Equal("invocation-id", record.InvocationId);
        Assert.Equal(WorkerPodEventGenerator.WorkerSource, record.Source);
    }

    [Fact]
    public void Format_UsesSharedWorkerPodConsoleContract()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        WorkerPodEventGenerator.WorkerSystemLogRecord record = new(
            4,
            "WorkerProxyStarted",
            WorkerSystemLoggerProvider.ApplicationCategory,
            string.Empty,
            "WorkerProxy started.",
            Timestamp,
            string.Empty,
            string.Empty,
            string.Empty);

        string formatted = generator.Format(record);

        Assert.StartsWith(
            $"{WorkerPodEventGenerator.EventStreamName} ",
            formatted,
            StringComparison.Ordinal);
        Assert.Contains(
            $",WorkerProxyStarted,{WorkerSystemLoggerProvider.ApplicationCategory},",
            formatted,
            StringComparison.Ordinal);
        Assert.Contains("\"WorkerProxy started.\"", formatted, StringComparison.Ordinal);
        Assert.Contains(Timestamp.UtcDateTime.ToString("O"), formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_UsesHostStringNormalization()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();

        string formatted = generator.Format(
            new WorkerPodEventGenerator.WorkerSystemLogRecord(
                3,
                "TestEvent",
                WorkerSystemLoggerProvider.ApplicationCategory,
                string.Empty,
                "first \"value\"\r\nsecond value",
                Timestamp,
                string.Empty,
                string.Empty,
                string.Empty));

        Assert.Contains("\"first 'value' second value\"", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_DoesNotApplyCustomFieldLimits()
    {
        using WorkerPodEventGenerator generator = CreateEventGenerator();
        string value = string.Concat(new string('a', 5_000), "customer-secret-sentinel");

        WorkerPodEventGenerator.WorkerSystemLogRecord record = new(
            3,
            value,
            WorkerSystemLoggerProvider.ApplicationCategory,
            string.Empty,
            value,
            Timestamp,
            string.Empty,
            string.Empty,
            string.Empty);
        string formatted = generator.Format(record);

        Assert.Equal(value, record.EventName);
        Assert.Equal(value, record.Summary);
        Assert.Contains("customer-secret-sentinel", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformTelemetry_WritesProxyAndWorkerRecordsThroughBackgroundConsolePipeline()
    {
        StringWriter output = new();
        using WorkerPodEventGenerator eventGenerator = new(output, TimeProvider.System);
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
        ILogger proxyLogger = provider.CreateLogger(WorkerSystemLoggerProvider.ApplicationCategory);
        ILogger workerLogger = provider.CreateLogger(WorkerSystemLoggerProvider.WorkerCategory);

        proxyLogger.LogInformation(new EventId(1, "WorkerProxyStarted"), "WorkerProxy started.");
        RpcLog workerLog = CreateWorkerLog(RpcLog.Types.Level.Warning);
        workerLog.EventId = "WorkerFailure";
        workerLog.InvocationId = "invocation-id";
        workerLogger.LogWorkerEvent(workerLog);
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
        Assert.Contains(
            lines,
            line => line.Contains(
                WorkerSystemLoggerProvider.ApplicationCategory,
                StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Worker.LanguageWorker", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("WorkerFailure", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("invocation-id", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("worker-system-message", StringComparison.Ordinal));
    }

    [Fact]
    public void LoggerProvider_PassesReadinessDestinationWithHostExceptionDetails()
    {
        const string sentinel = "customer-secret-sentinel";
        Uri destination = new("http://worker.internal:4321/functions");
        CapturingEventGenerator eventGenerator = new();
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
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
    }

    private static WorkerPodEventGenerator CreateEventGenerator()
    {
        return new(new StringWriter(), TimeProvider.System);
    }

    private static CapturedProxyLog CaptureWorkerRecord(RpcLog log)
    {
        CapturingEventGenerator eventGenerator = new();
        using WorkerSystemLoggerProvider provider = new(eventGenerator);
        ILogger logger = provider.CreateLogger(WorkerSystemLoggerProvider.WorkerCategory);

        logger.LogWorkerEvent(log);

        return Assert.Single(eventGenerator.ProxyRecords);
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

        public void LogEvent(
            LogLevel level,
            string eventName,
            string source,
            string details,
            string summary,
            string exceptionType,
            string exceptionMessage,
            string invocationId)
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
