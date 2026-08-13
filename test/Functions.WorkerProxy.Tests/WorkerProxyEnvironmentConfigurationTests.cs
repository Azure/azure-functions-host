// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy;
using Azure.Functions.WorkerProxy.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

[Collection(nameof(EnvironmentVariableCollection))]
public class WorkerProxyEnvironmentConfigurationTests
{
    [Fact]
    public async Task ProductionConfiguration_UsesEnvironmentAndOverridesAmbientHostingUrls()
    {
        int managementPort = GetAvailablePort();
        int ambientPort;
        do
        {
            ambientPort = GetAvailablePort();
        }
        while (ambientPort == managementPort);

        using EnvironmentVariableScope managementPortVariable =
            new(
                WorkerProxyOptions.ManagementPortConfigurationKey,
                managementPort.ToString(CultureInfo.InvariantCulture));
        using EnvironmentVariableScope urls =
            new("ASPNETCORE_URLS", $"http://127.0.0.1:{ambientPort}");
        using EnvironmentVariableScope preferHostingUrls =
            new("ASPNETCORE_PREFERHOSTINGURLS", bool.TrueString);
        await using WebApplication app = WorkerProxyApplication.Build([]);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await app.StartAsync(timeout.Token);

        Uri address = GetServerAddress(app);
        Assert.Equal(managementPort, address.Port);
        using HttpClient client = new() { BaseAddress = address };
        using HttpResponseMessage response =
            await client.GetAsync("/admin/instance/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static int GetAvailablePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static Uri GetServerAddress(WebApplication app)
    {
        IServer server = app.Services.GetRequiredService<IServer>();
        IServerAddressesFeature addresses =
            server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not publish a server address.");
        Uri address = new(Assert.Single(addresses.Addresses));

        return new UriBuilder(address) { Host = IPAddress.Loopback.ToString() }.Uri;
    }
}
