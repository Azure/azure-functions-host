// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.WorkerProxy.Tests;

internal sealed class WorkerProxyWebApplicationFactory : WebApplicationFactory<global::Program>
{
    private readonly IReadOnlyDictionary<string, string?> _configurationValues;

    public WorkerProxyWebApplicationFactory(IReadOnlyDictionary<string, string?>? configurationValues = null, bool useKestrel = false)
    {
        Dictionary<string, string?> values = configurationValues is null ? [] : new Dictionary<string, string?>(configurationValues);
        values.TryAdd(FunctionRpcRelayOptions.RuntimeGrpcPortKey, "0");
        values.TryAdd(FunctionRpcRelayOptions.WorkerGrpcPortKey, "0");
        _configurationValues = values;

        if (useKestrel)
        {
            UseKestrel();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);
        builder.UseSetting(WebHostDefaults.PreferHostingUrlsKey, bool.FalseString);
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(_configurationValues);
        });
    }

    public HttpClient CreateWorkerProxyClient()
    {
        HttpClient client = CreateClient();
        Uri baseAddress = client.BaseAddress ?? throw new InvalidOperationException("Kestrel did not publish an address.");
        if (IPAddress.TryParse(baseAddress.Host.Trim('[', ']'), out IPAddress? address) &&
            (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
        {
            client.BaseAddress = new UriBuilder(baseAddress) { Host = IPAddress.Loopback.ToString() }.Uri;
        }

        return client;
    }

    public Uri GetFunctionRpcAddress(FunctionRpcRelaySide side)
    {
        FunctionRpcRelayServer server = Services.GetRequiredService<FunctionRpcRelayServer>();
        Uri address = side switch
        {
            FunctionRpcRelaySide.Runtime => server.RuntimeAddress,
            FunctionRpcRelaySide.Worker => server.WorkerAddress,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown relay side.")
        };
        UriBuilder normalizedAddress = new(address);
        if (IPAddress.TryParse(address.Host.Trim('[', ']'), out IPAddress? ipAddress)
            && (ipAddress.Equals(IPAddress.Any) || ipAddress.Equals(IPAddress.IPv6Any)))
        {
            normalizedAddress.Host = IPAddress.Loopback.ToString();
        }

        return normalizedAddress.Uri;
    }
}
