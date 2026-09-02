// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerHttpForwardingTests
{
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
        using HttpRequestMessage request = new(HttpMethod.Post, "/invoke?name=worker")
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        };
        request.Headers.TryAddWithoutValidation("x-correlation-id", "correlation-value");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("worker-value", response.Headers.GetValues("x-worker-header").Single());
        Assert.Equal("received:payload", await response.Content.ReadAsStringAsync());
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

        using HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ManagementListener_DoesNotForward()
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

    private static async Task<WebApplication> StartWorkerAsync(RequestDelegate handler)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
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
}
