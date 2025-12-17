using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Options;
using OutOfProcModel.Abstractions.Worker;

namespace OutOfProcModel.FunctionsHost.Grpc;

internal class GrpcWorkerFactory(IOptions<FunctionApplicationOptions> appOptions, IHttpProxyService httpProxy, ISharedMemoryManager sharedMemoryManager) : IWorkerFactory, IDisposable
{
    private readonly IOptions<FunctionApplicationOptions> _appOptions = appOptions ?? throw new ArgumentNullException(nameof(appOptions));
    private readonly IHttpProxyService httpProxy = httpProxy;
    private readonly ISharedMemoryManager sharedMemoryManager = sharedMemoryManager;

    public ValueTask<IWorker> Create(WorkerCreationContext context)
    {
        var worker = new GrpcWorker(context.Definition, appOptions, sharedMemoryManager, httpProxy);

        return new ValueTask<IWorker>(worker);
    }

    public void Dispose()
    {
    }
}