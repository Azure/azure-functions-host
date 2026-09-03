// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerHttpForwardingTests
{
    public static IEnumerable<object[]> ForwarderErrors =>
        Enum.GetValues<ForwarderError>()
            .Where(static error => error is not ForwarderError.None)
            .Select(static error => new object[] { error });

    [Fact]
    public async Task HttpListener_ForwardsStatusHeadersBodyAndQuery()
    {
        await using WebApplication worker = await StartWorkerAsync(async context =>
        {
            Assert.Equal("/worker/invoke", context.Request.Path);
            Assert.Equal("?name=worker", context.Request.QueryString.Value);
            Assert.Equal("correlation-value", context.Request.Headers["x-correlation-id"]);

            string requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.Headers["x-worker-header"] = "worker-value";
            await context.Response.WriteAsync($"received:{requestBody}");
        });
        Uri workerAddress = GetAddress(worker);
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                new Uri(workerAddress, "/worker/").AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using HttpClient client = factory.CreateHttpForwardingClient();
        using RequestActivityRecorder activityRecorder = new("/invoke");
        using HttpRequestMessage request = new(HttpMethod.Post, "/invoke?name=worker")
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        };
        request.Headers.TryAddWithoutValidation("x-correlation-id", "correlation-value");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("worker-value", response.Headers.GetValues("x-worker-header").Single());
        Assert.Equal("received:payload", await response.Content.ReadAsStringAsync());

        Activity activity = await activityRecorder.WaitForActivityAsync();
        AssertRequestActivity(activity, "POST", "/invoke");
        Assert.Null(activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwardingResultAttribute));
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
    }

    [Fact]
    public async Task HttpListener_DoesNotFollowRedirects()
    {
        int requestCount = 0;
        await using WebApplication worker = await StartWorkerAsync(context =>
        {
            Interlocked.Increment(ref requestCount);
            context.Response.Redirect("/redirect-target");
            return Task.CompletedTask;
        });
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] = GetAddress(worker).AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using HttpClient client = factory.CreateHttpForwardingClient();

        using HttpResponseMessage response = await client.GetAsync("/redirect");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task HttpListener_NoDestination_ReturnsServiceUnavailable()
    {
        await using WorkerProxyWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateHttpForwardingClient();
        using RequestActivityRecorder activityRecorder = new("/");

        using HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        Activity activity = await activityRecorder.WaitForActivityAsync();
        AssertRequestActivity(activity, "GET", "/");
        Assert.Equal(
            WorkerHttpForwardingTelemetry.DestinationNotConfiguredResult,
            activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwardingResultAttribute));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task HttpListener_DestinationNotReady_ReturnsServiceUnavailable()
    {
        int port = GetUnusedPort();
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                $"http://localhost:{port}",
            [$"{WorkerEndpointReadinessProbeOptions.SectionName}:{nameof(WorkerEndpointReadinessProbeOptions.RetryDelay)}"] =
                "00:00:00.010",
            [$"{WorkerEndpointReadinessProbeOptions.SectionName}:{nameof(WorkerEndpointReadinessProbeOptions.TotalTimeout)}"] =
                "00:00:00.100"
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using HttpClient client = factory.CreateHttpForwardingClient();
        using RequestActivityRecorder activityRecorder = new("/not-ready");

        using HttpResponseMessage response = await client.GetAsync("/not-ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        Activity activity = await activityRecorder.WaitForActivityAsync();
        AssertRequestActivity(activity, "GET", "/not-ready");
        Assert.Equal(
            WorkerHttpForwardingTelemetry.DestinationNotReadyResult,
            activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwardingResultAttribute));
        string readinessError = Assert.IsType<string>(
            activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwarderErrorAttribute));
        Assert.Contains(
            readinessError,
            [
                WorkerEndpointReadinessResult.ConnectionRefused.ToString(),
                WorkerEndpointReadinessResult.Timeout.ToString()
            ]);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task HttpListener_ForwarderError_InvalidatesReadinessAndRecovers()
    {
        await using WebApplication worker = await StartWorkerAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        Uri workerAddress = GetAddress(worker);
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                workerAddress.AbsoluteUri,
            [$"{WorkerEndpointReadinessProbeOptions.SectionName}:{nameof(WorkerEndpointReadinessProbeOptions.RetryDelay)}"] =
                "00:00:00.010",
            [$"{WorkerEndpointReadinessProbeOptions.SectionName}:{nameof(WorkerEndpointReadinessProbeOptions.TotalTimeout)}"] =
                "00:00:00.100"
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using HttpClient client = factory.CreateHttpForwardingClient();
        WorkerEndpointReadinessProbe readinessProbe = factory.Services.GetRequiredService<WorkerEndpointReadinessProbe>();

        using (HttpResponseMessage response = await client.GetAsync("/warmup"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.True(readinessProbe.IsKnownReady(workerAddress));

        using TcpListener unrelatedListener = new(IPAddress.Loopback, 0);
        unrelatedListener.Start();
        Uri unrelatedDestination = new($"http://localhost:{((IPEndPoint)unrelatedListener.LocalEndpoint).Port}");
        Assert.True(await readinessProbe.WaitForReadyAsync(unrelatedDestination, CancellationToken.None));

        await worker.StopAsync();
        using RequestActivityRecorder activityRecorder = new("/forwarding-failure");

        using HttpResponseMessage failedResponse = await client.GetAsync("/forwarding-failure");

        Assert.Equal(HttpStatusCode.BadGateway, failedResponse.StatusCode);
        Assert.False(readinessProbe.IsKnownReady(workerAddress));
        Assert.True(readinessProbe.IsKnownReady(unrelatedDestination));

        Activity activity = await activityRecorder.WaitForActivityAsync();
        AssertRequestActivity(activity, "GET", "/forwarding-failure");
        Assert.Equal(
            WorkerHttpForwardingTelemetry.ForwarderErrorResult,
            activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwardingResultAttribute));
        Assert.Equal(
            Yarp.ReverseProxy.Forwarder.ForwarderError.Request.ToString(),
            activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwarderErrorAttribute));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);

        using (HttpResponseMessage unavailableResponse = await client.GetAsync("/unavailable"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailableResponse.StatusCode);
        }

        Assert.False(readinessProbe.IsKnownReady(workerAddress));

        await using WebApplication restartedWorker = await StartWorkerAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return Task.CompletedTask;
        }, workerAddress.Port);

        using HttpResponseMessage recoveredResponse = await client.GetAsync("/recovered");

        Assert.Equal(HttpStatusCode.Accepted, recoveredResponse.StatusCode);
        Assert.True(readinessProbe.IsKnownReady(workerAddress));
    }

    [Fact]
    public async Task HttpListener_CallerCancellation_PreservesCachedReadiness()
    {
        TaskCompletionSource requestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using WebApplication worker = await StartWorkerAsync(async context =>
        {
            if (context.Request.Path == "/warmup")
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            requestStarted.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
            }
        });
        Uri workerAddress = GetAddress(worker);
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                workerAddress.AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using HttpClient client = factory.CreateHttpForwardingClient();
        WorkerEndpointReadinessProbe readinessProbe = factory.Services.GetRequiredService<WorkerEndpointReadinessProbe>();
        using RequestActivityRecorder activityRecorder = new("/cancel", client.BaseAddress!.Port);

        using (HttpResponseMessage response = await client.GetAsync("/warmup"))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using CancellationTokenSource cancellation = new();
        Task<HttpResponseMessage> requestTask = client.GetAsync("/cancel", cancellation.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await requestTask);
        Activity activity = await activityRecorder.WaitForActivityAsync();
        Assert.Null(activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwardingResultAttribute));
        Assert.Null(activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwarderErrorAttribute));
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.True(readinessProbe.IsKnownReady(workerAddress));
    }

    [Fact]
    public async Task HttpListener_ResponseStartedForwarderError_InvalidatesReadiness()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Uri workerAddress = new($"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}");
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                workerAddress.AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(
            configuration,
            services => services.AddSingleton<IHttpForwarder>(
                new TestForwarder(ForwarderError.ResponseBodyDestination, startResponse: true)));
        using HttpClient client = factory.CreateHttpForwardingClient();
        WorkerEndpointReadinessProbe readinessProbe = factory.Services.GetRequiredService<WorkerEndpointReadinessProbe>();
        using RequestActivityRecorder activityRecorder = new("/partial-response", client.BaseAddress!.Port);

        using HttpResponseMessage response = await client.GetAsync("/partial-response");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Activity activity = await activityRecorder.WaitForActivityAsync();
        Assert.Equal(
            WorkerHttpForwardingTelemetry.ForwarderErrorResult,
            activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwardingResultAttribute));
        Assert.Equal(
            ForwarderError.ResponseBodyDestination.ToString(),
            activity.GetTagItem(WorkerHttpForwardingTelemetry.ForwarderErrorAttribute));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.False(readinessProbe.IsKnownReady(workerAddress));
    }

    [Theory]
    [MemberData(nameof(ForwarderErrors))]
    public async Task HttpListener_NonCanceledForwarderError_InvalidatesReadiness(ForwarderError error)
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Uri workerAddress = new($"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}");
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                workerAddress.AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(
            configuration,
            services => services.AddSingleton<IHttpForwarder>(new TestForwarder(error, startResponse: false)));
        using HttpClient client = factory.CreateHttpForwardingClient();
        WorkerEndpointReadinessProbe readinessProbe = factory.Services.GetRequiredService<WorkerEndpointReadinessProbe>();

        using HttpResponseMessage response = await client.GetAsync("/forwarder-error");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.False(readinessProbe.IsKnownReady(workerAddress));
    }

    [Fact]
    public async Task ManagementListener_DoesNotForwardNonAdminPath()
    {
        await using WebApplication worker = await StartWorkerAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return Task.CompletedTask;
        });
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] = GetAddress(worker).AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using HttpClient client = factory.CreateWorkerProxyClient();

        using HttpResponseMessage response = await client.GetAsync("/not-a-management-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HttpListener_UnknownAdminPath_DoesNotForward()
    {
        int requestCount = 0;
        await using WebApplication worker = await StartWorkerAsync(context =>
        {
            Interlocked.Increment(ref requestCount);
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return Task.CompletedTask;
        });
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                GetAddress(worker).AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using HttpClient client = factory.CreateHttpForwardingClient();

        using HttpResponseMessage response = await client.GetAsync("/admin/worker/ready");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, requestCount);
    }

    private static async Task<WebApplication> StartWorkerAsync(RequestDelegate handler, int port = 0)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        WebApplication app = builder.Build();
        app.Run(handler);
        await app.StartAsync();
        return app;
    }

    private static Uri GetAddress(WebApplication app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        string address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new Uri(address);
    }

    private static int GetUnusedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void AssertRequestActivity(Activity activity, string method, string path)
    {
        Assert.Equal("Microsoft.AspNetCore", activity.Source.Name);
        Assert.Equal(ActivityKind.Server, activity.Kind);
        Assert.Equal(method, activity.GetTagItem("http.request.method"));
        Assert.Equal("http", activity.GetTagItem("url.scheme"));
        Assert.Equal(path, activity.GetTagItem("url.path"));
        Assert.Single(activity.TagObjects.Where(tag => string.Equals(
            tag.Key, "http.request.method", StringComparison.Ordinal)));
        Assert.Single(activity.TagObjects.Where(tag => string.Equals(
            tag.Key, "url.scheme", StringComparison.Ordinal)));
        Assert.Single(activity.TagObjects.Where(tag => string.Equals(
            tag.Key, "url.path", StringComparison.Ordinal)));
    }

    private sealed class RequestActivityRecorder : IDisposable
    {
        private readonly TaskCompletionSource<Activity> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ActivityListener _listener;

        public RequestActivityRecorder(string requestPath, int? serverPort = null)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(
                    source.Name, "Microsoft.AspNetCore", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (string.Equals(
                        activity.GetTagItem("url.path") as string,
                        requestPath,
                        StringComparison.Ordinal)
                        && (serverPort is null || Equals(activity.GetTagItem("server.port"), serverPort.Value)))
                    {
                        _completion.TrySetResult(activity);
                    }
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public async Task<Activity> WaitForActivityAsync()
        {
            return await _completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }

    private sealed class TestForwarder(ForwarderError error, bool startResponse) : IHttpForwarder
    {
        public async ValueTask<ForwarderError> SendAsync(
            HttpContext context,
            string destinationPrefix,
            HttpMessageInvoker httpClient,
            ForwarderRequestConfig requestConfig,
            HttpTransformer transformer)
        {
            if (startResponse)
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                await context.Response.StartAsync();
            }

            return error;
        }
    }
}
