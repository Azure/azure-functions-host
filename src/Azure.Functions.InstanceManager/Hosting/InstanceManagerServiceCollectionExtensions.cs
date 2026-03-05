using Azure.Functions.InstanceManager;
using Microsoft.Azure.Functions.InstanceManager;
using Microsoft.Azure.Functions.InstanceManager.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Extensions.DependencyInjection;

public static class InstanceManagerServiceCollectionExtensions
{
    /// <summary>
    /// When WorkerModel:DisableProcessManagement is "true", the host will not start or
    /// manage worker processes itself. An external component (e.g. the Wrapper / Sidecar in
    /// the Worker Model prototype) is responsible for process lifecycle.
    /// </summary>
    private static bool IsProcessManagementDisabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("WorkerModel__DisableProcessManagement"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    public static IServiceCollection AddInstanceManagerScriptHostServices(this IServiceCollection services)
    {
        // These are always needed for configuration resolution regardless of process management mode.
        services.AddSingleton<IWebHostWorkerManager, WorkerManager>();
        services.ConfigureOptions<FunctionApplicationOptionsSetup>();
        services.AddSingleton<IWorkerConfigurationProvider, AppRootWorkerConfigurationProvider>();

        if (IsProcessManagementDisabled())
        {
            // External process management: skip hosted service, process factories, and job object registry.
            // Register no-op implementations for required interfaces so DI doesn't fail.
            services.AddSingleton<IProcessRegistry, EmptyProcessRegistry>();
        }
        else
        {
            // Standard mode: host manages worker process lifecycle.
            services.AddHostedService<InstanceManagerHostedService>();
            services.AddSingleton<IRpcWorkerProcessFactory, RpcWorkerProcessFactory>();
            services.AddSingleton<IWorkerProcessFactory, DefaultWorkerProcessFactory>();
            services.AddSingleton<IProcessRegistry, JobObjectRegistry>();
        }

        return services;
    }
}
