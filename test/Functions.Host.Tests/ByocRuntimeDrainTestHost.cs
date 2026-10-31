// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Host.Tests;

internal sealed class ByocRuntimeDrainTestHost : IAsyncDisposable
{
    private const string AuthenticationScheme = "ByocRuntimeDrainTest";
    private readonly IHost _host;

    private ByocRuntimeDrainTestHost(IHost host)
    {
        _host = host;
        Client = host.GetTestClient();
    }

    internal HttpClient Client { get; }

    internal static async Task<ByocRuntimeDrainTestHost> StartAsync(
        IByocRuntimeDrainManager manager,
        CancellationToken cancellationToken,
        bool includeCompute = true)
    {
        IHost host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseSetting(
                        WebHostDefaults.ApplicationKey,
                        typeof(Startup).Assembly.GetName().Name)
                    .UseTestServer()
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureServices(services =>
                    {
                        IMvcBuilder mvcBuilder = services.AddMvc(options =>
                        {
                            options.EnableEndpointRouting = false;
                        }).AddNewtonsoftJson();
                        mvcBuilder.ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                        });

                        if (includeCompute)
                        {
                            services.AddSingleton(manager);
                            mvcBuilder.AddApplicationPart(
                                typeof(ByocRuntimeDrainController).Assembly);
                        }

                        services.AddAuthentication(AuthenticationScheme)
                            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                                AuthenticationScheme,
                                _ =>
                                {
                                });
                        services.AddAuthorization(options =>
                        {
                            options.AddPolicy(
                                PolicyNames.AdminAuthLevel,
                                policy =>
                                {
                                    policy.AddAuthenticationSchemes(AuthenticationScheme);
                                    policy.RequireAuthenticatedUser();
                                });
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseAuthentication();
                        app.UseMvc();
                    });
            })
            .Build();

        try
        {
            await host.StartAsync(cancellationToken);
            return new ByocRuntimeDrainTestHost(host);
        }
        catch
        {
            await DisposeHostAsync(host);
            throw;
        }
    }

    internal async Task<HttpResponseMessage> PostAsync(
        string? json,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            "/admin/functions/byocdrain");
        request.Content = new StringContent(
            json ?? string.Empty,
            Encoding.UTF8,
            "application/json");
        if (authenticated)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(AuthenticationScheme, "admin");
        }

        return await Client.SendAsync(request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        using CancellationTokenSource timeout =
            new(TimeSpan.FromSeconds(10));
        try
        {
            await _host.StopAsync(timeout.Token);
        }
        finally
        {
            await DisposeHostAsync(_host);
        }
    }

    private static async Task DisposeHostAsync(IHost host)
    {
        if (host is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().AsTask().WaitAsync(
                TimeSpan.FromSeconds(10));
        }
        else
        {
            host.Dispose();
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!string.Equals(
                Request.Headers.Authorization.ToString(),
                $"{AuthenticationScheme} admin",
                StringComparison.Ordinal))
            {
                return Task.FromResult(
                    AuthenticateResult.Fail("Missing test admin authentication."));
            }

            ClaimsPrincipal principal = new(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "test-admin")],
                    AuthenticationScheme));
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        principal,
                        AuthenticationScheme)));
        }
    }
}
