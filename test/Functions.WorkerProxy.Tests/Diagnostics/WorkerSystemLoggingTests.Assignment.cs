// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Diagnostics;

public partial class WorkerSystemLoggingTests
{
    [Theory]
    [InlineData(201, "WorkerAssignmentRequestCompleted", LogLevel.Information)]
    [InlineData(204, "WorkerAssignmentRequestCompleted", LogLevel.Information)]
    [InlineData(400, "WorkerAssignmentRequestRejected", LogLevel.Warning)]
    [InlineData(409, "WorkerAssignmentRequestRejected", LogLevel.Warning)]
    [InlineData(500, "WorkerAssignmentRequestFailed", LogLevel.Error)]
    [InlineData(503, "WorkerAssignmentRequestFailed", LogLevel.Error)]
    public async Task AssignmentLogging_ClassifiesResponseWithoutChangingStatus(int statusCode, string eventName, LogLevel level)
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        WorkerPodStateManager manager = new(Options.Create(new WorkerProxyOptions { PodName = "test-pod" }), TimeProvider.System);
        Assert.True(manager.OnWorkerAttached(7));
        Assert.True(manager.OnWorkerStartStream(7, "worker-id"));
        DefaultHttpContext context = CreateAssignmentContext();
        WorkerAssignmentLoggingMiddleware middleware = new(http =>
        {
            http.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }, factory.CreateLogger<WorkerAssignmentLoggingMiddleware>(), manager);

        await middleware.InvokeAsync(context);

        CapturedProxyLog record = Assert.Single(generator.ProxyRecords);
        Assert.Equal(eventName, record.EventName);
        Assert.Equal(level, record.Level);
        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Contains(statusCode.ToString(CultureInfo.InvariantCulture), record.Message, StringComparison.Ordinal);
        Assert.Equal("worker-id", record.Context!.WorkerId);
        Assert.Equal(7, record.Context.ProxySessionId);
        Assert.Same(manager.State, record.Context.State);
        Assert.Equal(statusCode, record.Context.StatusCode);
        Assert.Equal("Assignment", record.Context.Operation);
        Assert.Contains("Worker attached and identified: True.", record.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Unexpected")]
    [InlineData("Canceled")]
    [InlineData("BadRequest")]
    [InlineData("ServerError")]
    public async Task AssignmentLogging_LogsAndRethrowsOriginalExceptionWithoutDefaultSuccessStatus(string failureKind)
    {
        const string sentinel = "private-assignment-value";
        Exception failure = failureKind switch
        {
            "BadRequest" => new BadHttpRequestException(sentinel, 400),
            "ServerError" => new BadHttpRequestException(sentinel, 503),
            "Canceled" => new OperationCanceledException(sentinel),
            _ => new InvalidOperationException(sentinel)
        };
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        WorkerPodStateManager manager = new(Options.Create(new WorkerProxyOptions { PodName = "test-pod" }), TimeProvider.System);
        DefaultHttpContext context = CreateAssignmentContext();
        WorkerAssignmentLoggingMiddleware middleware = new(_ => Task.FromException(failure),
            factory.CreateLogger<WorkerAssignmentLoggingMiddleware>(), manager);

        Exception? actual = await Record.ExceptionAsync(() => middleware.InvokeAsync(context));

        Assert.Same(failure, actual);
        Assert.Equal(200, context.Response.StatusCode);
        CapturedProxyLog record = Assert.Single(generator.ProxyRecords);
        bool rejected = string.Equals(failureKind, "BadRequest", StringComparison.Ordinal);
        Assert.Equal(rejected ? "WorkerAssignmentRequestRejected" : "WorkerAssignmentRequestFailed", record.EventName);
        Assert.Equal(rejected ? LogLevel.Warning : LogLevel.Error, record.Level);
        Assert.DoesNotContain("HTTP status: 200", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTP status 200", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, record.Message, StringComparison.Ordinal);
        Assert.Empty(record.Details);
        Assert.Empty(record.ExceptionMessage);
        if (failure is BadHttpRequestException badRequest)
        {
            Assert.Contains(badRequest.StatusCode.ToString(CultureInfo.InvariantCulture), record.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("HTTP status: unavailable.", record.Message, StringComparison.Ordinal);
        }

        if (!rejected)
        {
            Assert.Contains(failure.GetType().FullName!, record.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(true, true, "WorkerAssignmentRequestCanceled", LogLevel.Information)]
    [InlineData(false, true, "WorkerAssignmentRequestFailed", LogLevel.Error)]
    [InlineData(true, false, "WorkerAssignmentRequestFailed", LogLevel.Error)]
    public async Task AssignmentLogging_OnlyDowngradesCancellationForAbortedRequests(
        bool requestAborted, bool cancellationException, string eventName, LogLevel level)
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory factory = CreateLoggerFactory(generator);
        WorkerPodStateManager manager = new(Options.Create(new WorkerProxyOptions { PodName = "test-pod" }), TimeProvider.System);
        Assert.True(manager.OnWorkerAttached(7));
        Assert.True(manager.OnWorkerStartStream(7, "worker-id"));
        using CancellationTokenSource cancellation = new();
        if (requestAborted)
        {
            cancellation.Cancel();
        }

        DefaultHttpContext context = CreateAssignmentContext();
        context.RequestAborted = cancellation.Token;
        const string sentinel = "private-assignment-value";
        Exception failure = cancellationException
            ? new OperationCanceledException(sentinel, cancellation.Token)
            : new InvalidOperationException(sentinel);
        WorkerAssignmentLoggingMiddleware middleware = new(_ => Task.FromException(failure),
            factory.CreateLogger<WorkerAssignmentLoggingMiddleware>(), manager);

        Exception? actual = await Record.ExceptionAsync(() => middleware.InvokeAsync(context));

        Assert.Same(failure, actual);
        CapturedProxyLog record = Assert.Single(generator.ProxyRecords);
        Assert.Equal(eventName, record.EventName);
        Assert.Equal(level, record.Level);
        Assert.Equal("worker-id", record.Context!.WorkerId);
        Assert.Equal(7, record.Context.ProxySessionId);
        Assert.Same(manager.State, record.Context.State);
        Assert.Equal(requestAborted && cancellationException ? "Caller" : cancellationException ? "Unknown" : null, record.Context.CancellationSource);
        Assert.DoesNotContain(sentinel, record.Message, StringComparison.Ordinal);
        Assert.Empty(record.Details);
        Assert.Empty(record.ExceptionMessage);
        if (requestAborted && cancellationException)
        {
            Assert.Contains("canceled by the caller", record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("HTTP status", record.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignmentLogging_MalformedJsonIsLoggedInBothBindingModes(bool throwOnBadRequest)
    {
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
        {
            services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = throwOnBadRequest);
            services.RemoveAll<IWorkerSystemEventGenerator>();
            services.AddSingleton<IWorkerSystemEventGenerator>(generator);
        });
        using HttpClient client = factory.CreateWorkerProxyClient();
        using StringContent content = new("{", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PutAsync("/admin/worker/assignment", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        CapturedProxyLog record = Assert.Single(generator.ProxyRecords, record =>
            record.EventName.StartsWith("WorkerAssignmentRequest", StringComparison.Ordinal));
        Assert.Equal("WorkerAssignmentRequestRejected", record.EventName);
        Assert.Contains("HTTP status 400", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssignmentLogging_RecordsAuthenticationRejectionWithoutReadingOrPublishingAssignment()
    {
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
        {
            services.RemoveAll<IWorkerSystemEventGenerator>();
            services.AddSingleton<IWorkerSystemEventGenerator>(generator);
        });
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        WorkerPodState before = manager.State;
        using HttpClient client = factory.CreateWorkerProxyClient(authenticate: false);
        using StringContent content = new("""{"private-setting":"private-assignment-value"}""", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PutAsync("/admin/worker/assignment", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Same(before, manager.State);
        CapturedProxyLog record = Assert.Single(generator.ProxyRecords, entry =>
            entry.EventName.StartsWith("WorkerAssignmentRequest", StringComparison.Ordinal));
        Assert.Equal("WorkerAssignmentRequestRejected", record.EventName);
        Assert.Contains("HTTP status 401", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-assignment-value", record.Message, StringComparison.Ordinal);
        Assert.Empty(record.Details);
        Assert.Empty(record.ExceptionMessage);
    }

    private static DefaultHttpContext CreateAssignmentContext()
    {
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Put;
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/admin/worker/assignment"), 0, EndpointMetadataCollection.Empty, "Assignment"));
        return context;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignmentLogging_UsesAtomicDecisionSnapshotAfterRemoval(bool rejected)
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory logs = CreateLoggerFactory(generator);
        WorkerPodStateManager manager = new(Options.Create(new WorkerProxyOptions { PodName = "test-pod" }), TimeProvider.System);
        WorkerAssignRequest request = new()
        {
            StartupMode = "Preconfigured",
            FunctionAppName = "accepted-app",
            FunctionGroupName = "http",
            IsAlwaysReady = false,
            Environment = new() { ["SECRET"] = "private-environment" },
            FunctionAppDirectory = "private-directory"
        };
        if (rejected)
        {
            ManagementApiHandlers.AssignWorker(request, manager);
            request = new()
            {
                StartupMode = request.StartupMode,
                FunctionAppName = "rejected-app",
                FunctionGroupName = request.FunctionGroupName,
                IsAlwaysReady = request.IsAlwaysReady,
                Environment = request.Environment,
                FunctionAppDirectory = request.FunctionAppDirectory
            };
        }

        WorkerPodState? decision = null;
        DefaultHttpContext context = CreateAssignmentContext();
        WorkerAssignmentLoggingMiddleware middleware = new(http =>
        {
            ManagementApiHandlers.AssignWorker(request, manager, http);
            decision = manager.State;
            http.Response.StatusCode = rejected ? 409 : 201;
            manager.RequestRemoval();
            return Task.CompletedTask;
        }, logs.CreateLogger<WorkerAssignmentLoggingMiddleware>(), manager);

        await middleware.InvokeAsync(context);

        CapturedProxyLog record = Assert.Single(generator.ProxyRecords);
        Assert.Same(decision, record.Context!.State);
        Assert.Equal("accepted-app", record.Context.State!.FunctionAppName);
        Assert.Equal(rejected ? "AssignmentConflict" : "Created", record.Context.ReasonCode);
        Assert.Null(manager.State.FunctionAppName);
        Assert.DoesNotContain("rejected-app", record.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-", record.ToString(), StringComparison.Ordinal);
    }
}
