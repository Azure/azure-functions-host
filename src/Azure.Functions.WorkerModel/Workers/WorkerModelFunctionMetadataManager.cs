using System.Collections.Immutable;
using Microsoft.Azure.Functions.WorkerModel.Configuration;
using Microsoft.Azure.Functions.WorkerModel.JobHost;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Options;
using OutOfProcModel.Mock;

namespace Microsoft.Azure.Functions.WorkerModel.Workers;

internal sealed class WorkerModelFunctionMetadataManager : IFunctionMetadataManagerEx
{
    private readonly IJobHostManager _jobHostManager;
    private readonly IOptions<FunctionApplicationOptions> _appOptions;

    private static ApplicationDefinition DefaultApplicationDefinition = default!;
    private ImmutableArray<FunctionMetadata> _metadata;

    public WorkerModelFunctionMetadataManager(IJobHostManager jobHostManager, IOptions<FunctionApplicationOptions> appOptions)
    {
        _jobHostManager = jobHostManager;
        _appOptions = appOptions;
    }

    public ImmutableDictionary<string, ImmutableArray<string>> Errors => new Dictionary<string, ImmutableArray<string>>().ToImmutableDictionary();

    public ImmutableArray<FunctionMetadata> GetFunctionMetadata(bool forceRefresh = false, bool applyAllowlist = true, bool includeCustomProviders = true)
    {
        return GetFunctionMetadataAsync(forceRefresh, applyAllowlist, includeCustomProviders).GetAwaiter().GetResult();
    }

    public async Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(bool forceRefresh = false, bool applyAllowlist = true, bool includeCustomProviders = true)
    {
        // TODO: Right now everything is default-named
        DefaultApplicationDefinition ??= new ApplicationDefinition(_appOptions.Value.DefaultApplicationId, _appOptions.Value.DefaultApplicationVersion);

        if (!(await _jobHostManager.TryGetJobHostAsync(DefaultApplicationDefinition, out OutOfProcModel.Mock.JobHost host))
            || host is null)
        {
            throw new InvalidOperationException($"JobHost for ApplicationId '{_appOptions.Value.DefaultApplicationId}' not found.");
        }

        _metadata = await host.MetadataProvider.GetFunctionMetadataAsync(null, forceRefresh: true);
        return _metadata;
    }

    public bool TryGetFunctionMetadata(string functionName, out FunctionMetadata functionMetadata, bool forceRefresh = false)
    {
        functionMetadata = _metadata.Where(f => f.Name == functionName).FirstOrDefault()!;
        return functionMetadata != null;
    }
}
