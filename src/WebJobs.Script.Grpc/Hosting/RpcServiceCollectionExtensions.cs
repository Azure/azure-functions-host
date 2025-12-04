// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Rpc.Configuration;
using Microsoft.Azure.WebJobs.Script.Rpc.Hosting;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class RpcServiceCollectionExtensions
{
    public static IServiceCollection AddRpcScriptHostServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // HTTP Worker
        services.AddSingleton<IHttpWorkerProcessFactory, HttpWorkerProcessFactory>();
        services.AddSingleton<IHttpWorkerChannelFactory, HttpWorkerChannelFactory>();
        services.AddSingleton<IHttpWorkerService, DefaultHttpWorkerService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFunctionProvider, HttpWorkerFunctionProvider>());

        //Worker Function Invocation dispatcher
        services.AddSingleton<IFunctionInvocationDispatcherFactory, FunctionInvocationDispatcherFactory>();

        // Rpc Worker
        services.AddSingleton<IJobHostRpcWorkerChannelManager, JobHostRpcWorkerChannelManager>();
        services.AddSingleton<IRpcFunctionInvocationDispatcherLoadBalancer, RpcFunctionInvocationDispatcherLoadBalancer>();

        services.AddSingleton<IHostedService, WorkerConcurrencyManager>();

        // Configuration
        services.AddSingleton<IPostConfigureOptions<ScriptHostRecycleOptions>, HttpScriptHostRecycleOptionsSetup>();
        services.ConfigureOptions<HttpWorkerOptionsSetup>();
        services.ConfigureOptions<RpcFunctionMetadataOptionsSetup>();
        services.ConfigureOptions<RpcScriptHostRecycleOptionsSetup>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FunctionInvocationDispatcherShutdownManager>());

        services.AddSingleton<IWorkerFunctionDescriptorProviderFactory, RpcWorkerFunctionDescriptorProviderFactory>();
        services.AddSingleton<IScriptHostLifecycleService, RpcScriptHostLifecycleService>();
        services.AddSingleton<IScriptHostWorkerManager, RpcScriptHostWorkerManager>();

        return services;
    }

    public static IServiceCollection AddCommonRpcServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Add Language Worker Service
        services.AddSingleton<IRpcWorkerProcessFactory, RpcWorkerProcessFactory>();

        services.AddSingleton<IWorkerProcessFactory, DefaultWorkerProcessFactory>();
        services.TryAddSingleton<IWebHostRpcWorkerChannelManager, WebHostRpcWorkerChannelManager>();
        services.TryAddSingleton<IWorkerFunctionMetadataProvider, WorkerFunctionMetadataProvider>();
        services.AddSingleton<IScriptHostWorkerManager, RpcScriptHostWorkerManager>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConcurrencyThrottleProvider, WorkerChannelThrottleProvider>());
        services.AddSingleton<IWebHostWorkerManager, RpcWebHostWorkerManager>();

        services.AddManagedHostedService<RpcInitializationService>();

        AddProcessRegistry(services);

        return services;
    }

    private static void AddProcessRegistry(IServiceCollection services)
    {
        // W3WP already manages job objects
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && !ScriptSettingsManager.Instance.IsAppServiceEnvironment)
        {
            services.AddSingleton<IProcessRegistry, JobObjectRegistry>();
        }
        else
        {
            services.AddSingleton<IProcessRegistry, EmptyProcessRegistry>();
        }
    }
}
