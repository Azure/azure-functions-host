// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

[Collection(nameof(EnvironmentVariableCollection))]
public class WorkerProxyEnvironmentConfigurationTests
{
    [Theory]
    [InlineData(null, "legion-pod", "container-pod", "legion-pod")]
    [InlineData("explicit-pod", "legion-pod", "container-pod", "legion-pod")]
    [InlineData(null, null, "container-pod", "container-pod")]
    [InlineData("explicit-pod", null, "container-pod", "container-pod")]
    [InlineData("explicit-pod", "", "container-pod", "container-pod")]
    [InlineData("explicit-pod", " \t", "container-pod", "container-pod")]
    [InlineData("explicit-pod", "legion-pod", null, "legion-pod")]
    [InlineData(null, "legion-pod", " ", "legion-pod")]
    public async Task ProductionConfiguration_ResolvesPlatformPodIdentity(
        string? explicitPodName, string? platformPodName, string? containerName, string expectedPodName)
    {
        using EnvironmentVariableScope explicitIdentity = new("WORKERPROXY__PODNAME", explicitPodName);
        using EnvironmentVariableScope platformIdentity = new("WEBSITE_POD_NAME", platformPodName);
        using EnvironmentVariableScope containerIdentity = new("CONTAINER_NAME", containerName);
        await using WebApplication app = WorkerProxyApplication.Build([]);

        Assert.Equal(expectedPodName, app.Services.GetRequiredService<IOptions<WorkerProxyOptions>>().Value.PodName);
        Assert.Equal(expectedPodName, app.Services.GetRequiredService<WorkerPodStateManager>().State.PodName);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("explicit-pod", null, null)]
    [InlineData("explicit-pod", "", " ")]
    [InlineData("explicit-pod", " \t", " \t")]
    public void ProductionConfiguration_RejectsAbsentPlatformIdentity(
        string? explicitPodName, string? platformPodName, string? containerName)
    {
        using EnvironmentVariableScope explicitIdentity = new("WORKERPROXY__PODNAME", explicitPodName);
        using EnvironmentVariableScope platformIdentity = new("WEBSITE_POD_NAME", platformPodName);
        using EnvironmentVariableScope containerIdentity = new("CONTAINER_NAME", containerName);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => WorkerProxyApplication.Build([]));

        Assert.Contains(exception.Failures, failure => failure.Contains(nameof(WorkerProxyOptions.PodName), StringComparison.Ordinal));
    }

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
        using EnvironmentVariableScope httpPort = new("WORKERPROXY__HTTPPORT", "0");
        using EnvironmentVariableScope podName = new("WEBSITE_POD_NAME", "environment-worker-pod");
        using EnvironmentVariableScope explicitIdentity = new("WORKERPROXY__PODNAME", "ignored-worker-pod");
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
        Assert.Equal("environment-worker-pod", app.Services.GetRequiredService<WorkerPodStateManager>().State.PodName);
        using HttpClient client = new() { BaseAddress = address };
        using HttpResponseMessage response = await client.GetAsync("/admin/instance/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using HttpResponseMessage stateResponse = await client.GetAsync("/admin/worker/state", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        using JsonDocument state = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal("environment-worker-pod", state.RootElement.GetProperty("podName").GetString());
    }

    private static int GetAvailablePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
