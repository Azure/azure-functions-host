using Microsoft.Azure.Functions.InstanceManager.Configuration;
using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Azure.Functions.InstanceManager;

internal sealed class InstanceManagerHostedService : BackgroundService
{
    private readonly FunctionApplicationOptions _appOptions;
    private readonly GrpcServerOptions _grpcServerOptions;
    private readonly IRpcWorkerProcessFactory _processFactory;
    private readonly LanguageWorkerOptions _workerOptions;
    private readonly TaskCompletionSource _processLifetime = new();

    public InstanceManagerHostedService(
        IRpcWorkerProcessFactory processFactory,
        IOptions<LanguageWorkerOptions> workerOptions,
        IOptions<FunctionApplicationOptions> appOptions,
        IOptions<GrpcServerOptions> grpcServerOptions)
    {
        _processFactory = processFactory;
        _workerOptions = workerOptions.Value;
        _appOptions = appOptions.Value;
        _grpcServerOptions = grpcServerOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerConfig = _workerOptions.WorkerConfigs.Single(c => c.Description.Language == _appOptions.FunctionsWorkerRuntime);

        IWorkerProcess workerProcess = _processFactory.Create(Guid.NewGuid().ToString(), _appOptions.FunctionsWorkerRuntime, _appOptions.ApplicationRoot, workerConfig);

        await workerProcess.StartProcessAsync();
        workerProcess.Process.Exited += (sender, args) =>
        {
            _processLifetime.SetResult();
        };

        await _processLifetime.Task;
    }
}
