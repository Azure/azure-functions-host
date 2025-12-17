using Azure.Functions.InstanceManager;
using Microsoft.Azure.Functions.InstanceManager;
using Microsoft.Azure.Functions.InstanceManager.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Extensions.DependencyInjection;

public static class InstanceManagerServiceCollectionExtensions
{
    public static IServiceCollection AddInstanceManagerScriptHostServices(this IServiceCollection services)
    {
        services.AddHostedService<InstanceManagerHostedService>();
        services.AddSingleton<IWebHostWorkerManager, WorkerManager>();

        services.ConfigureOptions<FunctionApplicationOptionsSetup>();

        services.AddSingleton<IWorkerConfigurationProvider, AppRootWorkerConfigurationProvider>();

        services.AddSingleton<IRpcWorkerProcessFactory, RpcWorkerProcessFactory>();
        services.AddSingleton<IWorkerProcessFactory, DefaultWorkerProcessFactory>();
        services.AddSingleton<IProcessRegistry, JobObjectRegistry>();

        return services;
    }
}
