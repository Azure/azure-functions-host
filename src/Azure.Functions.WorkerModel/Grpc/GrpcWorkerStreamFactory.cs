using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.Functions.WorkerModel.Workers;
using Microsoft.Extensions.Options;
using OutOfProcModel.FunctionsHost.Grpc;
using OutOfProcModel.Mock;

namespace Microsoft.Azure.Functions.WorkerModel.Grpc;

internal class GrpcWorkerStreamFactory
{
    private readonly IJobHostManager _jobHostManager;
    private readonly IOptions<FunctionApplicationOptions> _appOptions;
    private readonly WorkerModelFunctionMetadataProvider _metadataProvider;

    public GrpcWorkerStreamFactory(IJobHostManager jobHostManager, IOptions<FunctionApplicationOptions> appOptions, WorkerModelFunctionMetadataProvider metadataProvider)
    {
        _jobHostManager = jobHostManager;
        _appOptions = appOptions;
        _metadataProvider = metadataProvider;
    }

    public GrpcWorkerStream CreateStream() => new GrpcWorkerStream(_jobHostManager, _appOptions, _metadataProvider);
}
