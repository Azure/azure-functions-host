using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.Functions.InstanceManager.Configuration;

internal sealed class AppRootWorkerConfigurationProvider : WorkerConfigurationProviderBase
{
    private readonly ILogger _logger;
    private readonly FunctionApplicationOptions _appOptions;

    public AppRootWorkerConfigurationProvider(
        ILoggerFactory loggerFactory,
        IMetricsLogger metricsLogger,
        IWorkerProfileManager workerProfileManager,
        ISystemRuntimeInformation systemRuntimeInformation,
        IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions,
        IOptions<FunctionApplicationOptions> appOptions)
        : base(metricsLogger, workerProfileManager, systemRuntimeInformation, workerConfigurationResolverOptions)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<AppRootWorkerConfigurationProvider>();
        _appOptions = appOptions.Value;
    }

    public override ILogger Logger => _logger;

    public override int Priority => 0;

    public override void PopulateWorkerConfigs(Dictionary<string, RpcWorkerConfig> configs)
    {
        AddProvider(WorkerResolverOptions, string.Empty, _appOptions.ApplicationRoot, configs);
    }
}
