// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering external worker services.
/// </summary>
internal static class ExternalWorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registers WebHost-level services required for external (compute-separated) workers.
    /// Must be called before <see cref="RpcServiceCollectionExtensions.AddCommonRpcServices"/>
    /// so that <c>TryAddSingleton</c> registrations do not override the external worker implementations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration used to read external worker settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddExternalWorkerServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Configure options from configuration (backed by environment variables)
        services.Configure<ExternalWorkerOptions>(options =>
        {
            options.IsEnabled = true;
            options.GrpcEndpoint = configuration[EnvironmentSettingNames.FunctionsWorkerExternalGrpcEndpoint];
        });

        // Core singletons
        services.AddSingleton<HostJsonContentProvider>();
        services.AddSingleton<IConnectedWorkerChannelFactory, ConnectedWorkerChannelFactory>();
        services.AddSingleton<IConnectedWorkerChannelManager, ConnectedWorkerChannelManager>();
        services.AddSingleton<IOutboundGrpcClientFactory, OutboundGrpcClientFactory>();

        // WorkerConnectionService must start (and block until worker handshake completes)
        // before WebJobsScriptHostService builds the ScriptHost.
        services.AddSingleton<WorkerConnectionService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<WorkerConnectionService>());
        services.AddSingleton<IWorkerConnectionManager>(sp => sp.GetRequiredService<WorkerConnectionService>());

        // Replace the default metadata provider. This registration must happen before
        // AddCommonRpcServices(), which uses TryAddSingleton<IWorkerFunctionMetadataProvider>.
        services.AddSingleton<IWorkerFunctionMetadataProvider, ConnectedWorkerFunctionMetadataProvider>();

        return services;
    }

    /// <summary>
    /// Registers ScriptHost-level services for external workers, including the
    /// <see cref="IFunctionInvocationDispatcherFactory"/> replacement.
    /// Called from within the ScriptHost builder's <c>ConfigureServices</c> callback.
    /// </summary>
    /// <param name="services">The ScriptHost service collection.</param>
    /// <param name="rootServiceProvider">The WebHost root service provider for forwarding singletons.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddExternalWorkerScriptHostServices(this IServiceCollection services, IServiceProvider rootServiceProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootServiceProvider);

        // Forward the WebHost-level channel manager into the ScriptHost container
        services.AddSingleton<IConnectedWorkerChannelManager>(
            rootServiceProvider.GetRequiredService<IConnectedWorkerChannelManager>());

        // Forward the external metadata provider so ScriptHost uses it instead of WorkerFunctionMetadataProvider
        services.AddSingleton<IWorkerFunctionMetadataProvider>(
            rootServiceProvider.GetRequiredService<IWorkerFunctionMetadataProvider>());

        // External workers provide their own metadata; script file validation is not applicable.
        services.Configure<FunctionMetadataOptions>(o => o.SkipScriptFileValidation = true);

        // External workers don't use the local filesystem for app content.
        // Disable directory creation and file watching.
        services.Configure<ScriptJobHostOptions>(o => o.FileWatchingEnabled = false);
        services.PostConfigure<ScriptApplicationHostOptions>(o => o.IsFileSystemReadOnly = true);

        // Register the external invocation dispatcher and its factory,
        // overriding the default FunctionInvocationDispatcherFactory registered by AddRpcScriptHostServices.
        services.AddSingleton<ConnectedWorkerInvocationDispatcher>();
        services.AddSingleton<IFunctionInvocationDispatcherFactory, ExternalFunctionInvocationDispatcherFactory>();

        return services;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the <c>FUNCTIONS_WORKER_EXTERNAL_ENABLED</c>
    /// setting is set to <c>true</c> or <c>1</c>.
    /// </summary>
    public static bool IsExternalWorkerEnabled(this IConfiguration configuration)
    {
        if (configuration is null)
        {
            return false;
        }

        string value = configuration[EnvironmentSettingNames.FunctionsWorkerExternalEnabled];

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal);
    }
}
