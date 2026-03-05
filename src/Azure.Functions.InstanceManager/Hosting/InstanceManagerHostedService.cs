using Microsoft.Azure.Functions.InstanceManager.Configuration;
using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.InstanceManager;

internal sealed class InstanceManagerHostedService : BackgroundService
{
    private readonly FunctionApplicationOptions _appOptions;
    private readonly GrpcServerOptions _grpcServerOptions;
    private readonly IRpcWorkerProcessFactory _processFactory;
    private readonly LanguageWorkerOptions _workerOptions;
    private readonly ILogger<InstanceManagerHostedService> _logger;
    private readonly TaskCompletionSource _processLifetime = new();

    public InstanceManagerHostedService(
        IRpcWorkerProcessFactory processFactory,
        IOptions<LanguageWorkerOptions> workerOptions,
        IOptions<FunctionApplicationOptions> appOptions,
        IOptions<GrpcServerOptions> grpcServerOptions,
        ILogger<InstanceManagerHostedService> logger)
    {
        _processFactory = processFactory;
        _workerOptions = workerOptions.Value;
        _appOptions = appOptions.Value;
        _grpcServerOptions = grpcServerOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Safety check: if process management is disabled (external Wrapper/Sidecar manages workers),
        // do not start any worker processes from the host.
        if (string.Equals(
            Environment.GetEnvironmentVariable("WorkerModel__DisableProcessManagement"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("WorkerModel:DisableProcessManagement is enabled. Skipping worker process startup — external process management assumed.");
            return;
        }

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
