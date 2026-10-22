// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Authentication;
using Azure.Functions.Tests;
using Azure.Functions.WorkerProxy.Authentication;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;

namespace Azure.Functions.WorkerProxy.Tests;

internal sealed class WorkerProxyWebApplicationFactory : WebApplicationFactory<global::Program>
{
    private readonly IReadOnlyDictionary<string, string?> _configurationValues;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly bool _useTestAuthentication;
    private readonly TestCertificateFiles _certificates = new();
    private readonly bool _supplyHttpOrigin;

    public WorkerProxyWebApplicationFactory(
        IReadOnlyDictionary<string, string?>? configurationValues = null, Action<IServiceCollection>? configureServices = null,
        bool useTestAuthentication = true)
    {
        Dictionary<string, string?> values = configurationValues is null ? [] : new Dictionary<string, string?>(configurationValues);
        _supplyHttpOrigin = !values.ContainsKey("WorkerProxy:HttpProxyEndpoint");
        values.TryAdd("WEBSITE_POD_NAME", "test-worker-pod");
        values.TryAdd(RuntimeWorkerAuthentication.WorkerKeySetting, RuntimeAuthenticationTestData.EncodedWorkerKey);
        values.TryAdd(RuntimeWorkerAuthentication.AppKeySetting, RuntimeAuthenticationTestData.EncodedAppKey);
        values.TryAdd("WorkerProxy:CertificatePath", _certificates.CertificatePath);
        values.TryAdd("WorkerProxy:CertificateKeyPath", _certificates.KeyPath);
        values.TryAdd($"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.ManagementPort)}", "0");
        values.TryAdd($"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.RuntimeGrpcPort)}", "0");
        values.TryAdd($"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerGrpcPort)}", "0");
        values.TryAdd($"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.HttpPort)}", "0");
        _configurationValues = values;
        _configureServices = configureServices;
        _useTestAuthentication = useTestAuthentication;
        UseKestrel();
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
        builder.ConfigureServices(services =>
        {
            if (_useTestAuthentication)
            {
                Mock<IRuntimeAuthenticationKeyProvider> keys = new();
                keys.Setup(provider => provider.GetKeys()).Returns(() => RuntimeAuthenticationTestData.Keys);
                services.Replace(ServiceDescriptor.Singleton(keys.Object));
            }

            _configureServices?.Invoke(services);
        });
    }

    public HttpClient CreateWorkerProxyClient()
    {
        return new HttpClient { BaseAddress = NormalizeAddress(GetEndpoints().GetManagementAddress()) };
    }

    public Uri GetFunctionRpcAddress(FunctionRpcRelaySide side)
    {
        return NormalizeAddress(GetEndpoints().GetRelayAddress(side));
    }

    public HttpClient CreateHttpForwardingClient(bool authenticate = true)
    {
        HttpClient client = new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = NormalizeAddress(GetEndpoints().GetHttpAddress())
        };
        if (authenticate)
        {
            string workerToken = RuntimeAuthenticationTestData.CreateCallerToken();
            client.DefaultRequestHeaders.Add(RuntimeWorkerAuthentication.WorkerTokenHeader, workerToken);
        }

        return client;
    }

    private WorkerProxyEndpointConfiguration GetEndpoints()
    {
        WorkerProxyEndpointConfiguration endpoints = Services.GetRequiredService<WorkerProxyEndpointConfiguration>();
        if (_supplyHttpOrigin)
        {
            Services.GetRequiredService<IOptions<WorkerProxyOptions>>().Value.HttpProxyEndpoint =
                NormalizeAddress(endpoints.GetHttpAddress()).AbsoluteUri;
        }

        return endpoints;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _certificates.Dispose();
    }

    private static Uri NormalizeAddress(Uri address)
    {
        UriBuilder normalizedAddress = new(address);
        if (IPAddress.TryParse(address.Host.Trim('[', ']'), out IPAddress? ipAddress)
            && (ipAddress.Equals(IPAddress.Any) || ipAddress.Equals(IPAddress.IPv6Any)))
        {
            normalizedAddress.Host = IPAddress.Loopback.ToString();
        }

        return normalizedAddress.Uri;
    }
}
