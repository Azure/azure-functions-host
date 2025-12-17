using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Host;

namespace Microsoft.Azure.Functions.WorkerModel.JobHost;

internal class WorkerScriptHostLifecycle : IScriptHostLifecycleService
{
    public Task InitializedAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
