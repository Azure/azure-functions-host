using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Microsoft.Azure.Functions.InstanceManager;

internal class WorkerManager : IWebHostWorkerManager
{
    public Task SpecializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task WorkerWarmupAsync()
    {
        return Task.CompletedTask;
    }
}
