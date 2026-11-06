// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Host.Controllers;
using Azure.Functions.Host.WorkerLink;
using Azure.Functions.Rpc.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Host.Tests;

internal sealed class WorkerLinkTestHost : IAsyncDisposable
{
    private readonly IHost _host;

    private WorkerLinkTestHost(IHost host, bool useKestrel)
    {
        _host = host;
        Client = useKestrel
            ? new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) }
            : host.GetTestClient();
    }

    internal HttpClient Client { get; }

    internal static async Task<WorkerLinkTestHost> StartAsync(CancellationToken cancellationToken,
        IWorkerChannelRegistry registry, bool includeCompute = true, bool useKestrel = false)
    {
        IHost host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                if (useKestrel)
                {
                    webBuilder.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
                }
                else
                {
                    webBuilder.UseTestServer();
                }

                webBuilder
                    .UseSetting(WebHostDefaults.ApplicationKey, typeof(Startup).Assembly.GetName().Name)
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureServices(services =>
                    {
                        IMvcBuilder mvcBuilder = services.AddMvc(options =>
                        {
                            options.EnableEndpointRouting = false;
                        }).AddNewtonsoftJson();

                        // Test assembly discovery can otherwise include the compute assembly even for the standard host.
                        mvcBuilder.ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.ApplicationParts.Add(new AssemblyPart(typeof(Startup).Assembly));
                        });

                        if (includeCompute)
                        {
                            services.AddSingleton(registry);
                            services.AddOptions<MvcOptions>().Configure<IOptions<JsonOptions>, ILoggerFactory>(static (options, json, loggerFactory) =>
                                options.InputFormatters.Insert(0, new WorkerLinkRequestInputFormatter(json.Value, loggerFactory.CreateLogger<SystemTextJsonInputFormatter>())));
                            mvcBuilder.AddApplicationPart(typeof(WorkerLinkController).Assembly);
                        }

                        services.AddAuthentication();
                        services.AddAuthorization(options =>
                        {
                            options.AddScriptPolicies();
                            // Deliberately permissive test authorization, not the production admin authentication policy.
                            options.AddPolicy(PolicyNames.AdminAuthLevel, policy => policy.RequireAssertion(_ => true));
                        });
                    })
                    .Configure(app => app.UseMvc());
            })
            .Build();

        try
        {
            await host.StartAsync(cancellationToken);
            return new WorkerLinkTestHost(host, useKestrel);
        }
        catch
        {
            await DisposeHostAsync(host);
            throw;
        }
    }

    internal async Task<HttpResponseMessage> PutAsync(string? json, CancellationToken cancellationToken,
        string workerId = "worker-pod-abc123")
    {
        using HttpRequestMessage request = new(HttpMethod.Put, $"/admin/workers/{workerId}");
        request.Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json");

        return await Client.SendAsync(request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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
            await disposable.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            host.Dispose();
        }
    }
}
