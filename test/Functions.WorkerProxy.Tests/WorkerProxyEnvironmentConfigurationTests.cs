// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

[Collection(nameof(EnvironmentVariableCollection))]
public class WorkerProxyEnvironmentConfigurationTests
{
    [Fact]
    public async Task ProductionConfiguration_UsesWorkerProxyEndpointOptions()
    {
        int managementPort = GetAvailablePort();
        int ambientPort;
        do
        {
            ambientPort = GetAvailablePort();
        }
        while (ambientPort == managementPort);

        using EnvironmentVariableScope managementPortVariable =
            new("WORKERPROXY__MANAGEMENTPORT", managementPort.ToString(CultureInfo.InvariantCulture));
        using EnvironmentVariableScope runtimeGrpcPort = new("WORKERPROXY__RUNTIMEGRPCPORT", "0");
        using EnvironmentVariableScope workerGrpcPort = new("WORKERPROXY__WORKERGRPCPORT", "0");
        using EnvironmentVariableScope urls = new("ASPNETCORE_URLS", $"http://127.0.0.1:{ambientPort}");
        using EnvironmentVariableScope dotnetSetting = new("DOTNET_WORKER_PROXY_TEST_SETTING", "preserved");
        await using WebApplication app = WorkerProxyApplication.Build([]);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await app.StartAsync(timeout.Token);

        WorkerProxyEndpointConfiguration endpoints = app.Services.GetRequiredService<WorkerProxyEndpointConfiguration>();
        Uri address = new UriBuilder(endpoints.GetManagementAddress()) { Host = IPAddress.Loopback.ToString() }.Uri;
        Assert.Equal(managementPort, address.Port);
        Assert.NotEqual(ambientPort, address.Port);
        Assert.Equal("preserved", app.Configuration["DOTNET_WORKER_PROXY_TEST_SETTING"]);
        using HttpClient client = new() { BaseAddress = address };
        using HttpResponseMessage response = await client.GetAsync("/admin/instance/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static int GetAvailablePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
