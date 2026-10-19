// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Azure.Functions.WorkerProxy.Tests;

internal sealed class WorkerProxyWebApplicationFactory : WebApplicationFactory<global::Program>
{
    private const string SiteTokenIssuer = "https://appservice.core.azurewebsites.net";
    private const string SiteTokenKey = "0123456789abcdef0123456789abcdef";
    private readonly IReadOnlyDictionary<string, string?> _configurationValues;
    private readonly Action<IServiceCollection>? _configureServices;

    public WorkerProxyWebApplicationFactory(
        IReadOnlyDictionary<string, string?>? configurationValues = null, Action<IServiceCollection>? configureServices = null)
    {
        Dictionary<string, string?> values = configurationValues is null ? [] : new Dictionary<string, string?>(configurationValues);
        values.TryAdd($"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.PodName)}", "test-worker-pod");
        values.TryAdd($"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.ManagementPort)}", "0");
        values.TryAdd($"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.RuntimeGrpcPort)}", "0");
        values.TryAdd($"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerGrpcPort)}", "0");
        values.TryAdd($"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.HttpPort)}", "0");
        values.TryAdd("CONTAINER_ENCRYPTION_KEY", Convert.ToHexString(Encoding.UTF8.GetBytes(SiteTokenKey)));
        _configurationValues = values;
        _configureServices = configureServices;
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
        builder.ConfigureServices(services => _configureServices?.Invoke(services));
    }

    public HttpClient CreateWorkerProxyClient(bool includeSiteToken = true)
    {
        HttpClient client = new() { BaseAddress = NormalizeAddress(GetEndpoints().GetManagementAddress()) };
        if (includeSiteToken)
        {
            client.DefaultRequestHeaders.Add(SiteTokenValidator.HeaderName, CreateSiteToken());
        }

        return client;
    }

    public static string CreateSiteToken(
        string issuer = SiteTokenIssuer,
        string audience = "test-worker-pod",
        DateTime? expires = null,
        string signingKey = SiteTokenKey,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(signingKey));
        JwtSecurityToken token = new(
            issuer,
            audience,
            [new Claim("sub", "worker-state-poller")],
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, algorithm));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public Uri GetFunctionRpcAddress(FunctionRpcRelaySide side)
    {
        return NormalizeAddress(GetEndpoints().GetRelayAddress(side));
    }

    public HttpClient CreateHttpForwardingClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = NormalizeAddress(GetEndpoints().GetHttpAddress())
        };
    }

    private WorkerProxyEndpointConfiguration GetEndpoints()
    {
        return Services.GetRequiredService<WorkerProxyEndpointConfiguration>();
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
