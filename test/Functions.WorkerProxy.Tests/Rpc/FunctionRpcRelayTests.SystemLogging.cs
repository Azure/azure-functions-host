// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string WorkerApplicationInsightsLoggingEnabled = nameof(WorkerApplicationInsightsLoggingEnabled);
    private const string WorkerOpenTelemetryEnabled = nameof(WorkerOpenTelemetryEnabled);

    [Theory]
    [InlineData(RpcLog.Types.Level.Trace, false)]
    [InlineData(RpcLog.Types.Level.Debug, false)]
    [InlineData(RpcLog.Types.Level.Information, true)]
    [InlineData(RpcLog.Types.Level.Warning, true)]
    [InlineData(RpcLog.Types.Level.Error, true)]
    [InlineData(RpcLog.Types.Level.Critical, true)]
    [InlineData(RpcLog.Types.Level.None, false)]
    public async Task Relay_SystemLogs_AreFilteredAndForwarded(
        RpcLog.Types.Level level,
        bool expectedInConsolePipeline)
    {
        CapturingEventGenerator eventGenerator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(eventGenerator);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await worker.WriteAsync(CreateStartStream("telemetry-worker"), timeout.Token);
        Assert.NotNull(await runtime.ReadAsync(timeout.Token));
        StreamingMessage log =
            CreateWorkerLog(RpcLog.Types.RpcLogCategory.System, "worker-system-log", level);

        await worker.WriteAsync(log, timeout.Token);

        Assert.Equal(log, await runtime.ReadAsync(timeout.Token));
        if (expectedInConsolePipeline)
        {
            Assert.Equal(log.RpcLog, Assert.Single(eventGenerator.WorkerLogs));
        }
        else
        {
            Assert.Empty(eventGenerator.WorkerLogs);
        }
    }

    [Fact]
    public async Task Relay_AssignmentLoggingConfiguration_DoesNotChangeFixedSystemLogFilter()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"worker-proxy-log-filter-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        const string hostJson =
            """
            {
              "version": "2.0",
              "logging": {
                "logLevel": {
                  "Worker.LanguageWorkerChannel": "Debug"
                }
              }
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(directory, "host.json"), hostJson);

        try
        {
            CapturingEventGenerator eventGenerator = new();
            await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(eventGenerator);
            using CancellationTokenSource timeout = new(TestTimeout);
            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
            await worker.WriteAsync(CreateStartStream("telemetry-worker"), timeout.Token);
            Assert.NotNull(await runtime.ReadAsync(timeout.Token));

            WorkerAssignRequest assignment = new()
            {
                StartupMode = "Preconfigured",
                FunctionAppName = "test-app",
                FunctionGroupName = "test-group",
                IsAlwaysReady = false,
                Environment = new()
                {
                    ["AzureFunctionsJobHost__logging__logLevel__Worker"] = "Debug"
                },
                FunctionAppDirectory = directory
            };
            using HttpClient managementClient = factory.CreateWorkerProxyClient();
            using HttpResponseMessage assignmentResponse =
                await managementClient.PutAsJsonAsync("/admin/worker/assignment", assignment, timeout.Token);
            Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);

            StreamingMessage debug =
                CreateWorkerLog(RpcLog.Types.RpcLogCategory.System, "worker-debug-log", RpcLog.Types.Level.Debug);
            await worker.WriteAsync(debug, timeout.Token);

            Assert.Equal(debug, await runtime.ReadAsync(timeout.Token));
            Assert.Empty(eventGenerator.WorkerLogs);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(RpcLog.Types.RpcLogCategory.User)]
    [InlineData(RpcLog.Types.RpcLogCategory.CustomMetric)]
    public async Task Relay_CustomerLogs_AreForwardedWithoutEnteringConsolePipeline(
        RpcLog.Types.RpcLogCategory category)
    {
        CapturingEventGenerator eventGenerator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(eventGenerator);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await worker.WriteAsync(CreateStartStream("telemetry-worker"), timeout.Token);
        Assert.NotNull(await runtime.ReadAsync(timeout.Token));
        const string sentinel = "customer-worker-log-secret";
        StreamingMessage log = CreateWorkerLog(category, sentinel);

        await worker.WriteAsync(log, timeout.Token);

        Assert.Equal(log, await runtime.ReadAsync(timeout.Token));
        Assert.Empty(eventGenerator.WorkerLogs);
        Assert.DoesNotContain(
            eventGenerator.ProxyRecords,
            record => record.EventName.Contains(sentinel, StringComparison.Ordinal)
                || record.Message.Contains(sentinel, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(WorkerApplicationInsightsLoggingEnabled)]
    [InlineData(WorkerOpenTelemetryEnabled)]
    public async Task Relay_DirectTelemetryCapability_DoesNotChangeWorkerLogRouting(string capability)
    {
        CapturingEventGenerator eventGenerator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(eventGenerator);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await InitializeWorkerAsync(
            worker,
            runtime,
            new Dictionary<string, string> { [capability] = bool.TrueString },
            timeout.Token);

        StreamingMessage log =
            CreateWorkerLog(RpcLog.Types.RpcLogCategory.System, "worker-system-log");
        await worker.WriteAsync(log, timeout.Token);

        Assert.Equal(log, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(log.RpcLog, Assert.Single(eventGenerator.WorkerLogs));
    }

    private static async Task InitializeWorkerAsync(
        RelayClient worker,
        RelayClient runtime,
        IDictionary<string, string>? capabilities,
        CancellationToken cancellationToken)
    {
        StreamingMessage startStream = CreateStartStream("telemetry-worker");
        await worker.WriteAsync(startStream, cancellationToken);
        Assert.Equal(startStream, await runtime.ReadAsync(cancellationToken));

        StreamingMessage initResponse = CreateInitResponse("telemetry-init", httpUri: null);
        if (capabilities is not null)
        {
            initResponse.WorkerInitResponse.Capabilities.Add(capabilities);
        }

        await worker.WriteAsync(initResponse, cancellationToken);
        Assert.Equal(initResponse, await runtime.ReadAsync(cancellationToken));
    }

    private static WorkerProxyWebApplicationFactory CreateSystemLoggingFactory(
        CapturingEventGenerator eventGenerator)
    {
        return new WorkerProxyWebApplicationFactory(configureServices: services =>
        {
            services.RemoveAll<IWorkerSystemEventGenerator>();
            services.AddSingleton<IWorkerSystemEventGenerator>(eventGenerator);
        });
    }

    private static StreamingMessage CreateWorkerLog(
        RpcLog.Types.RpcLogCategory category,
        string value,
        RpcLog.Types.Level level = RpcLog.Types.Level.Warning)
    {
        return new StreamingMessage
        {
            RequestId = value,
            RpcLog = new RpcLog
            {
                Category = value,
                EventId = value,
                Exception = new RpcException
                {
                    Message = value,
                    StackTrace = value
                },
                Level = level,
                LogCategory = category,
                Message = value,
                Properties = value,
                PropertiesMap =
                {
                    ["secret"] = new TypedData { String = value }
                }
            }
        };
    }

    private sealed class CapturingEventGenerator : IWorkerSystemEventGenerator
    {
        public ConcurrentQueue<RpcLog> WorkerLogs { get; } = [];

        public ConcurrentQueue<CapturedProxyLog> ProxyRecords { get; } = [];

        public void LogWorkerEvent(RpcLog log)
        {
            WorkerLogs.Enqueue(log.Clone());
        }

        public void LogProxyEvent(LogLevel level, string eventName, string message)
        {
            ProxyRecords.Enqueue(new(eventName, message));
        }
    }

    private sealed record CapturedProxyLog(string EventName, string Message);
}
