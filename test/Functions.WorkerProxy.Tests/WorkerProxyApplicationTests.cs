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
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Azure.Functions.WorkerProxy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

[Collection(nameof(EnvironmentVariableCollection))]
public class WorkerProxyApplicationTests
{
    [Fact]
    public async Task StartupProbe_UsesOnlyConfiguredManagementListener()
    {
        int managementPort = GetAvailablePort();
        int ambientPort;
        do
        {
            ambientPort = GetAvailablePort();
        }
        while (ambientPort == managementPort);

        using EnvironmentVariableScope urls =
            new("ASPNETCORE_URLS", $"http://127.0.0.1:{ambientPort}");
        using EnvironmentVariableScope httpPorts =
            new("ASPNETCORE_HTTP_PORTS", ambientPort.ToString(CultureInfo.InvariantCulture));
        using EnvironmentVariableScope preferHostingUrls =
            new("ASPNETCORE_PREFERHOSTINGURLS", bool.TrueString);
        using EnvironmentVariableScope kestrelEndpoint =
            new("Kestrel__Endpoints__Extra__Url", $"http://127.0.0.1:{ambientPort}");
        using EnvironmentVariableScope allowedHosts =
            new("ASPNETCORE_ALLOWEDHOSTS", "blocked.invalid");
        using EnvironmentVariableScope directAllowedHosts =
            new("AllowedHosts", "blocked.invalid");
        await using WebApplication app = WorkerProxyApplication.Build(
            ["--management-port", managementPort.ToString(CultureInfo.InvariantCulture)]);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await app.StartAsync(timeout.Token);

        Uri address = GetServerAddress(app);
        Assert.Equal(managementPort, address.Port);
        using HttpClient client = new() { BaseAddress = address };

        using HttpResponseMessage readyResponse =
            await client.GetAsync("/admin/instance/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        Assert.Empty(await readyResponse.Content.ReadAsByteArrayAsync(timeout.Token));

        using HttpResponseMessage unsupportedMethodResponse =
            await client.PostAsync("/admin/instance/ready", content: null, timeout.Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, unsupportedMethodResponse.StatusCode);

        using HttpResponseMessage unrelatedRouteResponse =
            await client.GetAsync("/admin/worker/ready", timeout.Token);
        Assert.Equal(HttpStatusCode.NotFound, unrelatedRouteResponse.StatusCode);
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
