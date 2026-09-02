// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerProxyKestrelTests
{
    [Fact]
    public async Task WorkerProxyPorts_ConfigureRootKestrelListeners()
    {
        int port = GetAvailablePort();
        Dictionary<string, string?> configurationValues = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.ManagementPort)}"] = port.ToString(CultureInfo.InvariantCulture)
        };
        await using WorkerProxyWebApplicationFactory factory = new(configurationValues);
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        Assert.Equal(port, client.BaseAddress!.Port);
        using HttpResponseMessage response =
            await client.GetAsync("/admin/instance/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        WorkerProxyEndpointConfiguration endpoints = factory.Services.GetRequiredService<WorkerProxyEndpointConfiguration>();
        Uri workerAddress = endpoints.GetRelayAddress(FunctionRpcRelaySide.Worker);
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(workerAddress.Host)));
        Assert.False(endpoints.TryGetRelaySide(endpoints.GetManagementAddress().Port, out _));
        Assert.True(endpoints.IsHttpForwardingPort(endpoints.GetHttpForwardingAddress().Port));
        Assert.False(endpoints.IsHttpForwardingPort(endpoints.GetManagementAddress().Port));

        using HttpClient runtimeClient = new() { BaseAddress = factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime) };
        using HttpRequestMessage runtimeRequest = new(HttpMethod.Get, "/admin/instance/ready")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        using HttpResponseMessage runtimeResponse = await runtimeClient.SendAsync(runtimeRequest, timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, runtimeResponse.StatusCode);
    }

    private static int GetAvailablePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
