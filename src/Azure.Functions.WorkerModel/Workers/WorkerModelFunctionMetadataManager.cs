using System.Collections.Immutable;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.Functions.WorkerModel.Workers;

internal sealed class WorkerModelFunctionMetadataManager : IFunctionMetadataManagerEx
{
    private readonly WorkerModelFunctionMetadataProvider _provider;

    public WorkerModelFunctionMetadataManager(WorkerModelFunctionMetadataProvider provider)
    {
        _provider = provider;
    }

    public ImmutableDictionary<string, ImmutableArray<string>> Errors => new Dictionary<string, ImmutableArray<string>>().ToImmutableDictionary();

    public ImmutableArray<FunctionMetadata> GetFunctionMetadata(bool forceRefresh = false, bool applyAllowlist = true, bool includeCustomProviders = true)
    {
        return _provider.GetMetadata();
    }

    public Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(bool forceRefresh = false, bool applyAllowlist = true, bool includeCustomProviders = true)
    {
        return Task.FromResult(_provider.GetMetadata());
    }

    public bool TryGetFunctionMetadata(string functionName, out FunctionMetadata functionMetadata, bool forceRefresh = false)
    {
        functionMetadata = _provider.GetMetadata().Where(f => f.Name == functionName).FirstOrDefault()!;
        return functionMetadata != null;
    }
}

internal sealed class WorkerModelFunctionMetadataProvider
{
    private ImmutableArray<FunctionMetadata> _metadata;

    public void SetMetadata(IEnumerable<FunctionMetadata> functionMetadata)
    {
        _metadata = functionMetadata.ToImmutableArray();
    }

    public ImmutableArray<FunctionMetadata> GetMetadata()
    {
        return _metadata;
    }
}
