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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public class WorkerProxyKestrelTests
{
    [Fact]
    public async Task HttpPorts_ConfiguresRealKestrelListener()
    {
        int port = GetAvailablePort();
        Dictionary<string, string?> configurationValues = new()
        {
            [WebHostDefaults.HttpPortsKey] = port.ToString(CultureInfo.InvariantCulture)
        };
        await using WorkerProxyWebApplicationFactory factory =
            new(configurationValues, useKestrel: true);
        using HttpClient client = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        Assert.Equal(port, client.BaseAddress!.Port);
        IServer server = factory.Services.GetRequiredService<IServer>();
        IServerAddressesFeature addresses =
            server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not publish a server address.");
        Assert.Single(addresses.Addresses);

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
}
