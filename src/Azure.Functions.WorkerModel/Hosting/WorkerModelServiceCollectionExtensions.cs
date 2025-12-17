using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.Functions.WorkerModel.Grpc;
using Microsoft.Azure.Functions.WorkerModel.JobHost;
using Microsoft.Azure.Functions.WorkerModel.Workers;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OutOfProcModel.Abstractions.Core;
using OutOfProcModel.Abstractions.Worker;
using OutOfProcModel.FunctionsHost.Grpc;
using OutOfProcModel.Mock;
using OutOfProcModel.Workers;

namespace Microsoft.Extensions.DependencyInjection;

public static class WorkersModelServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerModelScriptHostServices(this IServiceCollection services)
    {
        services.AddSingleton<MessageHandlerPipeline>();
        services.TryAddSingleton<IEventProcessor, WorkerEventProcessor>();
        services.TryAddSingleton<IWorkerResolver, DefaultWorkerResolver>();
        services.AddSingleton<DefaultWorkerManager>();
        services.AddSingleton<IFunctionMetadataProvider, WorkerModelFunctionMetadataProvider>();
        services.TryAddSingleton<IWorkerManager>(s => s.GetRequiredService<DefaultWorkerManager>());
        services.TryAddSingleton<IScriptHostWorkerManager>(s => s.GetRequiredService<DefaultWorkerManager>());
        services.AddSingleton<IWorkerFunctionDescriptorProviderFactory, WorkerModelFunctionDescriptorProviderFactory>();
        services.AddSingleton<IScriptHostLifecycleService, WorkerScriptHostLifecycle>();

        services.AddHttpForwarder();
        services.AddSingleton<IHttpProxyService, DefaultHttpProxyService>();

        return services;
    }

    public static IServiceCollection AddWorkerModelWebHostServices(this IServiceCollection services)
    {
        services.AddHostedService<RpcInitializationService>();
        services.ConfigureOptions<GrpcServerOptionsSetup>();
        services.ConfigureOptions<FunctionApplicationOptionsSetup>();
        services.AddSingleton<WorkerModelFunctionMetadataManager>();
        services.AddSingleton<IFunctionMetadataManager>(s => s.GetRequiredService<WorkerModelFunctionMetadataManager>());
        services.AddSingleton<IFunctionMetadataManagerEx>(s => s.GetRequiredService<WorkerModelFunctionMetadataManager>());
        services.AddSingleton<IJobHostManager, JobHostManager>();
        services.AddSingleton<GrpcWorkerStreamFactory>();

        return services;
    }

    public static IServiceCollection AddWorkerModelCommonServices(this IServiceCollection services)
    {
        return services;
    }
}