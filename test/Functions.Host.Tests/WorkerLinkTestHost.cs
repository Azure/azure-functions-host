// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Azure.Functions.Host.Tests;

internal sealed class WorkerLinkTestHost : IAsyncDisposable
{
    private const string RequestIdHeader = "X-Test-Worker-Link-Request";
    private readonly IHost _host;
    private readonly RequestObserver _observer;

    private WorkerLinkTestHost(IHost host, RequestObserver observer)
    {
        _host = host;
        _observer = observer;
        Client = host.GetTestClient();
    }

    internal HttpClient Client { get; }

    internal static async Task<WorkerLinkTestHost> StartAsync(CancellationToken cancellationToken, bool includeCompute = true)
    {
        RequestObserver observer = new();
        IHost host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseSetting(WebHostDefaults.ApplicationKey, typeof(Startup).Assembly.GetName().Name)
                    .UseTestServer()
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureServices(services =>
                    {
                        IMvcBuilder mvcBuilder = services.AddMvc(options =>
                        {
                            options.EnableEndpointRouting = false;
                            options.Filters.Add(observer);
                        }).AddNewtonsoftJson();

                        // Test assembly discovery can otherwise include the compute assembly even for the standard host.
                        mvcBuilder.ConfigureApplicationPartManager(parts =>
                        {
                            parts.ApplicationParts.Clear();
                            parts.ApplicationParts.Add(new AssemblyPart(typeof(Startup).Assembly));
                        });

                        if (includeCompute)
                        {
                            AddSharedChannelDependencies(services);
                            ClientWorkerComposition.Instance.ConfigureWebHostServices(services, mvcBuilder);
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
            return new WorkerLinkTestHost(host, observer);
        }
        catch
        {
            await DisposeHostAsync(host);
            throw;
        }
    }

    internal PendingRequest Put(string? json, CancellationToken cancellationToken)
    {
        string requestId = Guid.NewGuid().ToString("N");
        PendingRequest pending = new();
        _observer.Requests.TryAdd(requestId, pending);
        pending.Response = SendAsync(requestId, json, cancellationToken);

        return pending;
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

    private static void AddSharedChannelDependencies(IServiceCollection services)
    {
        Mock<IScriptHostManager> hostManager = new();
        hostManager.As<IServiceProvider>()
            .Setup(provider => provider.GetService(typeof(IOptions<ScriptJobHostOptions>)))
            .Returns(Options.Create(new ScriptJobHostOptions { RootScriptPath = "c:\\test" }));
        Mock<IOptionsMonitor<ScriptApplicationHostOptions>> applicationHostOptions = new();
        applicationHostOptions.SetupGet(options => options.CurrentValue)
            .Returns(new ScriptApplicationHostOptions { ScriptPath = "c:\\test" });
        Mock<IAppCapabilitiesStore> capabilities = new();
        capabilities.Setup(store => store.TrySetAll(It.IsAny<IEnumerable<KeyValuePair<string, string>>>()))
            .Returns(true);

        services.AddSingleton<IScriptEventManager, ScriptEventManager>();
        services.AddSingleton(hostManager.Object);
        services.AddSingleton(Mock.Of<IEnvironment>());
        services.AddSingleton(applicationHostOptions.Object);
        services.AddSingleton(Mock.Of<ISharedMemoryManager>());
        services.AddSingleton(Options.Create(new WorkerConcurrencyOptions()));
        services.AddSingleton(Options.Create(new FunctionsHostingConfigOptions()));
        services.AddSingleton(capabilities.Object);
        services.AddSingleton(Mock.Of<IHttpProxyService>());
        services.AddSingleton(Mock.Of<IMetricsLogger>());
    }

    private async Task<HttpResponseMessage> SendAsync(string requestId, string? json, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, "/admin/workers");
        request.Headers.Add(RequestIdHeader, requestId);
        request.Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json");

        return await Client.SendAsync(request, cancellationToken);
    }

    internal sealed class PendingRequest
    {
        internal TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task ActionInvoked => Invoked.Task;

        internal Task ActionCompleted => Completed.Task;

        internal Task<HttpResponseMessage> Response { get; set; } = null!;

        internal async Task WaitForProgressAsync(Task progress, CancellationToken cancellationToken)
        {
            await Task.WhenAny(progress, Response).WaitAsync(cancellationToken);
            if (Response.IsCompleted)
            {
                using HttpResponseMessage response = await Response;
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Worker link HTTP request completed before expected handshake progress: {(int)response.StatusCode} {body}");
            }

            await progress.WaitAsync(cancellationToken);
        }
    }

    private sealed class RequestObserver : IAsyncActionFilter
    {
        internal ConcurrentDictionary<string, PendingRequest> Requests { get; } = new(StringComparer.Ordinal);

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            Requests.TryRemove(context.HttpContext.Request.Headers[RequestIdHeader].ToString(), out PendingRequest? pending);
            try
            {
                // Invoke the real action up to its first await before releasing a concurrent test request.
                Task<ActionExecutedContext> action = next();
                pending?.Invoked.TrySetResult();
                await action;
            }
            finally
            {
                pending?.Completed.TrySetResult();
            }
        }
    }
}
