// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.WorkerProxy.Tests.Diagnostics;

public partial class WorkerSystemLoggingTests
{
    [Theory]
    [InlineData(ForwarderError.None, 204, false, false, null)]
    [InlineData(ForwarderError.None, 503, false, false, "WorkerServerError")]
    [InlineData(ForwarderError.Request, 200, false, false, "TransportFailure")]
    [InlineData(ForwarderError.ResponseBodyDestination, 202, true, false, "TransportFailure")]
    [InlineData(ForwarderError.RequestCanceled, 200, false, true, "Canceled")]
    public async Task HttpLogging_EmitsOneObservedOutcomeWithoutChangingForwarding(
        ForwarderError error, int status, bool started, bool canceled, string? outcome)
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory logs = CreateLoggerFactory(generator);
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        WorkerProxyOptions options = new()
        {
            PodName = "test-pod",
            WorkerHttpEndpoint = $"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}/private-path"
        };
        WorkerPodStateManager manager = new(Options.Create(options), TimeProvider.System);
        Assert.True(manager.OnWorkerAttached(1));
        Assert.True(manager.OnWorkerStartStream(1, "original-worker"));
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(new(
            WorkerStartupMode.Preconfigured, "original-app", "http", false, new Dictionary<string, string>(), "private-directory")));
        WorkerPodState snapshot = manager.State;
        Mock<IHttpForwarder> transport = new();
        transport.Setup(f => f.SendAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<HttpMessageInvoker>(),
            It.IsAny<ForwarderRequestConfig>(), It.IsAny<HttpTransformer>()))
            .Returns((HttpContext http, string _, HttpMessageInvoker _, ForwarderRequestConfig _, HttpTransformer _) =>
            {
                manager.RequestRemoval();
                http.Response.StatusCode = status;
                http.RequestAborted = new CancellationToken(canceled);
                return ValueTask.FromResult(error);
            });
        Mock<IHttpMessageHandlerFactory> handlers = new();
        using HttpClientHandler handler = new();
        handlers.Setup(f => f.CreateHandler(It.IsAny<string>())).Returns(handler);
        using WorkerHttpForwarder forwarder = new(transport.Object, handlers.Object);
        await using FunctionRpcRelay relay = new(logs.CreateLogger<FunctionRpcRelay>(), Mock.Of<IWorkerCapabilityFinalizer>(), manager);
        WorkerEndpointReadinessProbe probe = new(Options.Create(new WorkerEndpointReadinessProbeOptions()), logs.CreateLogger<WorkerEndpointReadinessProbe>());
        WorkerHttpForwardingMiddleware middleware = new(Options.Create(options), probe, forwarder, relay, manager,
            logs.CreateLogger<WorkerHttpForwardingMiddleware>());
        Mock<IHttpResponseFeature> response = new();
        response.SetupAllProperties();
        response.SetupGet(f => f.HasStarted).Returns(started);
        response.Object.Headers = new HeaderDictionary();
        DefaultHttpContext context = new() { TraceIdentifier = "request-1" };
        context.Features.Set(response.Object);
        context.Request.QueryString = new("?secret=private-query");
        context.Request.Headers.Authorization = "private-authorization";
        context.Request.Headers["x-ms-invocation-id"] = "untrusted-invocation";

        await middleware.InvokeAsync(context);

        transport.Verify(f => f.SendAsync(It.IsAny<HttpContext>(), options.WorkerHttpEndpoint, It.IsAny<HttpMessageInvoker>(),
            It.IsAny<ForwarderRequestConfig>(), It.IsAny<HttpTransformer>()), Times.Once);
        Assert.Equal(error is not ForwarderError.None && !started && !canceled ? 502 : status, context.Response.StatusCode);
        CapturedProxyLog[] outcomes = generator.ProxyRecords.Where(r =>
            string.Equals(r.EventName, "WorkerHttpOutcome", StringComparison.Ordinal)).ToArray();
        if (outcome is null)
        {
            Assert.Empty(outcomes);
        }
        else
        {
            CapturedProxyLog record = Assert.Single(outcomes);
            Assert.Equal(outcome, record.Context!.Outcome);
            Assert.Equal(error.ToString(), record.Context.ForwarderError);
            Assert.Equal(context.Response.StatusCode, record.Context.StatusCode);
            Assert.Equal(started, record.Context.ResponseStarted);
            Assert.Equal(canceled ? "Caller" : null, record.Context.CancellationSource);
            Assert.Same(snapshot, record.Context.State);
            Assert.Equal("original-worker", record.Context.WorkerId);
            Assert.Equal("request-1", record.Context.RequestId);
            Assert.Null(record.Context.InvocationId);
            Assert.True(record.Context.ElapsedMs >= 0);
            Assert.Equal(canceled ? LogLevel.Information : LogLevel.Warning, record.Level);
        }

        Assert.DoesNotContain(generator.ProxyRecords, r => r.Message.Contains("private-", StringComparison.Ordinal)
            || r.Message.Contains("untrusted-invocation", StringComparison.Ordinal));
        Assert.Equal(canceled || error is ForwarderError.None, probe.IsKnownReady(new Uri(options.WorkerHttpEndpoint)));
    }

    [Fact]
    public async Task HttpLogging_DefaultCategoryExportsWorkerErrorsButNotSuccesses()
    {
        CapturingEventGenerator generator = new();
        await using var worker = await WorkerHttpForwardingTests.StartWorkerAsync(context =>
        {
            context.Response.StatusCode = context.Request.Path.StartsWithSegments("/fail") ? 500 : 204;
            return Task.CompletedTask;
        });
        await using WorkerProxyWebApplicationFactory factory = new(new Dictionary<string, string?>
        {
            ["WorkerProxy:WorkerHttpEndpoint"] = WorkerHttpForwardingTests.GetAddress(worker).AbsoluteUri
        }, services =>
        {
            services.RemoveAll<IWorkerSystemEventGenerator>();
            services.AddSingleton<IWorkerSystemEventGenerator>(generator);
        });
        using HttpClient client = factory.CreateHttpForwardingClient();
        using HttpResponseMessage success = await client.GetAsync("/success");
        using HttpResponseMessage failure = await client.GetAsync("/fail?private=not-logged");

        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        CapturedProxyLog record = Assert.Single(generator.ProxyRecords, r =>
            string.Equals(r.EventName, "WorkerHttpOutcome", StringComparison.Ordinal));
        Assert.Equal("WorkerServerError", record.Context!.Outcome);
        Assert.Equal(WorkerProxyLogExporter.HttpForwardingCategory, record.Source);
        Assert.DoesNotContain(generator.ProxyRecords, r => r.ToString().Contains("not-logged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadinessLogging_EmitsOneBoundedFailureWithRequestContext()
    {
        CapturingEventGenerator generator = new();
        using ILoggerFactory logs = CreateLoggerFactory(generator);
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        WorkerEndpointReadinessProbe probe = new(Options.Create(new WorkerEndpointReadinessProbeOptions
        {
            RetryDelay = TimeSpan.FromMilliseconds(10),
            TotalTimeout = TimeSpan.FromMilliseconds(100)
        }), logs.CreateLogger<WorkerEndpointReadinessProbe>());
        WorkerSystemLogContext snapshot = WorkerSystemLogContext.FromState(WorkerPodState.CreateUnassigned("test-pod", Timestamp))
            with { RequestId = "readiness-request" };
        using IDisposable? scope = logs.CreateLogger(WorkerProxyLogExporter.HttpForwardingCategory).BeginScope(snapshot);

        WorkerEndpointReadinessResult result = await probe.WaitForReadyAsync(
            new Uri($"http://localhost:{port}/private-path?secret=private-query"), CancellationToken.None);

        Assert.NotEqual(WorkerEndpointReadinessResult.Ready, result);
        CapturedProxyLog record = Assert.Single(generator.ProxyRecords);
        Assert.Equal("NotReady", record.Context!.Outcome);
        Assert.Equal("Readiness", record.Context.Phase);
        Assert.Equal(result.ToString(), record.Context.ReasonCode);
        Assert.Equal("readiness-request", record.Context.RequestId);
        Assert.True(record.Context.Attempts > 0);
        Assert.True(record.Context.ElapsedMs >= 0);
        Assert.DoesNotContain("private-", record.ToString(), StringComparison.Ordinal);
    }
}
