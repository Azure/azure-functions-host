using Microsoft.Azure.WebJobs.Script.Workers;
using OutOfProcModel.Abstractions.Worker;

namespace OutOfProcModel.Workers;

internal class DefaultWorkerManager : IWorkerManager, IScriptHostWorkerManager, IDisposable
{
    // Dictionary mapping applicationId to a list of workers:
    private readonly IList<IWorker> _workers = [];

    private readonly IWorkerFactory _workerFactory;

    public DefaultWorkerManager(IWorkerFactory workerFactory)
    {
        _workerFactory = workerFactory;
    }

    public WorkerManagerState State => throw new NotImplementedException();

    // Create a worker and return a way for callers to monitor its state
    public async ValueTask<IWorkerState> CreateWorkerAsync(WorkerCreationContext workerCreationContext)
    {
        // this is JobHost-scoped, so ensure that we own lifetime of workers fully
        var worker = await _workerFactory.Create(workerCreationContext);
        _workers.Add(worker);
        return worker;
    }

    /// <summary>
    /// Removes the worker from load balancing...
    /// </summary>
    public async Task<bool> RemoveWorkerAsync(string workerId)
    {
        var worker = _workers.FirstOrDefault(w => w.Definition.WorkerId == workerId);

        if (worker == null)
        {
            return false; // Worker not found
        }

        _workers.Remove(worker);

        await worker.DrainAsync(TimeSpan.FromSeconds(5));

        (worker as IDisposable)?.Dispose();

        return true;
    }

    public IReadOnlyCollection<IWorker> GetWorkers()
    {
        return _workers.AsReadOnly();
    }

    public void Dispose()
    {
        foreach (var worker in _workers)
        {
            // Dispose of the worker if it implements IDisposable
            if (worker is IDisposable disposableWorker)
            {
                disposableWorker.Dispose();
            }
        }
    }

    public Task GetWorkerStatusesAsync()
    {
        throw new NotImplementedException();
    }

    public Task<bool> RestartWorkerWithInvocationIdAsync(string invocationId, Exception exception)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<WorkerProcessInfo>> GetWorkerProcessInfoAsync(string workerRuntime)
    {
        throw new NotImplementedException();
    }
}