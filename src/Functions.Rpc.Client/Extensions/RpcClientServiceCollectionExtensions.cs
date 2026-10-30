// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Azure.Functions.Rpc.Client;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Client worker service graph.
/// </summary>
public static class RpcClientServiceCollectionExtensions
{
    /// <summary>
    /// Adds the root-owned Client transport, channel factory, registry, and metadata services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddRpcClientServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ThrowIfRegistered<IRpcClientFactory>(services, nameof(AddRpcClientServices));

        services.AddHttpForwarder();
        services.TryAddSingleton<IHttpProxyService, DefaultHttpProxyService>();
        services.AddSingleton<WorkerProxyHttpTransport>();
        services.AddSingleton<Func<IHttpProxyService>>(provider => () => provider.GetRequiredService<WorkerProxyHttpTransport>());
        services.AddOptions<WorkerProcessCountOptions>(RpcClientWorkerChannelFactory.WorkerOptionsName)
            .BindConfiguration("RpcClient:Worker")
            .Validate(options => RpcClientWorkerChannelFactory.HasValidTimeouts(options),
                "RpcClient worker startup, initialization and reload timeouts must be positive and have a bounded total.")
            .ValidateOnStart();
        services.AddSingleton<IWorkerHostConfigurationParser, WorkerHostConfigurationParser>();
        services.AddOptions<TokenServiceProxyOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<TokenServiceProxyOptions>, TokenServiceProxyOptionsValidator<TokenServiceProxyOptions>>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(provider =>
        {
            TokenServiceProxyOptions options = provider.GetRequiredService<IOptions<TokenServiceProxyOptions>>().Value;
            return new TokenServiceProxyAdmission(options.MaxHostRequests, options.MaxHostBufferedBytes);
        });
        services.AddSingleton<IRpcClientFactory, RpcClientFactory>();
        services.AddSingleton<IDuplexChannelFactory<StreamingMessage>, FunctionRpcDuplexChannelFactory>();
        services.AddSingleton<IRpcClientWorkerChannelFactory, RpcClientWorkerChannelFactory>();
        services.AddSingleton<WorkerChannelRegistry>();
        services.AddSingleton<IWorkerChannelRegistry>(provider => provider.GetRequiredService<WorkerChannelRegistry>());
        services.AddSingleton<IHostConfigurationSourceProvider>(provider => provider.GetRequiredService<WorkerChannelRegistry>());
        services.AddSingleton<IWorkerFunctionMetadataProvider, RpcClientWorkerFunctionMetadataProvider>();
        services.Configure<FunctionMetadataOptions>(options => options.UseWorkerRuntimeFromMetadata = true);

        return services;
    }

    /// <summary>
    /// Adds the Client worker graph, worker-backed Host metadata, and first-link ScriptHost activation.
    /// </summary>
    /// <param name="services">The root service collection to update.</param>
    /// <param name="scriptHostFactory">Resolves the root-owned ScriptHost service whose activation is deferred.</param>
    /// <returns>The supplied service collection.</returns>
    /// <remarks>
    /// The supplied ScriptHost service is borrowed, not registered as an automatically started <see cref="IHostedService"/>.
    /// Use <see cref="AddRpcClientServices"/> when only transport and worker services are required, without ScriptHost activation.
    /// </remarks>
    public static IServiceCollection AddRpcClientWebHostServices(
        this IServiceCollection services, Func<IServiceProvider, IHostedService> scriptHostFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scriptHostFactory);

        services.AddRpcClientServices();
        services.Replace(ServiceDescriptor.Singleton<IFunctionMetadataProvider, RpcClientFunctionMetadataProvider>());
        services.AddSingleton(provider => new RpcClientScriptHostStartupCoordinator(
            provider.GetRequiredService<IWorkerChannelRegistry>(),
            scriptHostFactory(provider),
            provider.GetRequiredService<IHostApplicationLifetime>(),
            provider.GetRequiredService<ILogger<RpcClientScriptHostStartupCoordinator>>()));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<RpcClientScriptHostStartupCoordinator>());

        return services;
    }

    /// <summary>
    /// Adds Client services owned by one ScriptHost child container while borrowing the root-owned channel registry.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="rootServiceProvider">The root provider that owns the Client channel registry.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddRpcClientScriptHostServices(
        this IServiceCollection services,
        IServiceProvider rootServiceProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootServiceProvider);
        ThrowIfRegistered<IRpcClientFunctionInvocationDispatcher>(services, nameof(AddRpcClientScriptHostServices));

        IWorkerChannelRegistry channelRegistry = rootServiceProvider.GetService<IWorkerChannelRegistry>()
            ?? throw new InvalidOperationException(
                $"The root service provider must be configured with {nameof(AddRpcClientServices)} before configuring ScriptHost services.");
        IWorkerFunctionMetadataProvider metadataProvider = rootServiceProvider.GetService<IWorkerFunctionMetadataProvider>()
            ?? throw new InvalidOperationException(
                $"The root service provider must be configured with {nameof(AddRpcClientServices)} before configuring ScriptHost services.");

        // ScriptHost children borrow these root-owned instances so child disposal cannot tear down linked channels or
        // fork the metadata cache used by the root metadata manager.
        services.AddSingleton(channelRegistry);
        services.AddSingleton(metadataProvider);
        services.AddSingleton<IRpcClientFunctionInvocationDispatcher, RpcClientFunctionInvocationDispatcher>();
        services.AddSingleton<IFunctionInvocationDispatcherFactory, RpcClientFunctionInvocationDispatcherFactory>();
        services.AddSingleton<IScriptHostLifecycleService, RpcClientScriptHostLifecycleService>();
        services.AddSingleton<IScriptHostWorkerManager, RpcClientScriptHostWorkerManager>();
        services.AddRpcScriptHostCoreServices();
        services.Configure<FunctionMetadataOptions>(options => options.UseWorkerRuntimeFromMetadata = true);

        return services;
    }

    private static void ThrowIfRegistered<TService>(IServiceCollection services, string registrationMethod)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(TService)))
        {
            throw new InvalidOperationException($"{registrationMethod} has already been called for this service collection.");
        }
    }
}
