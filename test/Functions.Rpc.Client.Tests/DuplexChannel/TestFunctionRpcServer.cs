// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client.Tests;

internal sealed class TestFunctionRpcServer : IAsyncDisposable
{
    private readonly WebApplication _application;

    private TestFunctionRpcServer(WebApplication application, Uri endpoint, TestFunctionRpcService service)
    {
        _application = application;
        Endpoint = endpoint;
        Service = service;
    }

    internal Uri Endpoint { get; }

    internal TestFunctionRpcService Service { get; }

    internal static async Task<TestFunctionRpcServer> StartAsync(bool mapService = true)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });
        builder.Services.AddSingleton<TestFunctionRpcService>();
        builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = int.MaxValue;
            options.MaxSendMessageSize = int.MaxValue;
        });

        WebApplication application = builder.Build();
        if (mapService)
        {
            application.MapGrpcService<TestFunctionRpcService>();
        }

        await application.StartAsync();

        IServer server = application.Services.GetRequiredService<IServer>();
        IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>();
        Uri endpoint = new(addresses.Addresses.Single());
        TestFunctionRpcService service = application.Services.GetRequiredService<TestFunctionRpcService>();

        return new TestFunctionRpcServer(application, endpoint, service);
    }

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();
        await _application.DisposeAsync();
    }
}
