using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Extensions.Options;
using OutOfProcModel.FunctionsHost.Grpc;
using OutOfProcModel.Mock;

namespace Microsoft.Azure.Functions.WorkerModel.Grpc;

internal class GrpcWorkerStreamFactory
{
    private readonly IJobHostManager _jobHostManager;
    private readonly IOptions<FunctionApplicationOptions> _appOptions;

    public GrpcWorkerStreamFactory(IJobHostManager jobHostManager, IOptions<FunctionApplicationOptions> appOptions)
    {
        _jobHostManager = jobHostManager;
        _appOptions = appOptions;
    }

    public GrpcWorkerStream CreateStream() => new GrpcWorkerStream(_jobHostManager, _appOptions);
}
