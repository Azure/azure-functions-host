// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
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
        Assert.Equal(new WorkerSystemLogContext { WorkerId = "worker-id", ProxySessionId = 7 }, record.Context);
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

    private static DefaultHttpContext CreateAssignmentContext()
    {
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Put;
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/admin/worker/assignment"), 0, EndpointMetadataCollection.Empty, "Assignment"));
        return context;
    }
}
