using System.Collections.Immutable;
using Microsoft.Azure.Functions.WorkerModel.JobHost;
using Microsoft.Azure.WebJobs.Script.Description;
using OutOfProcModel.Abstractions.ControlPlane;
using OutOfProcModel.Abstractions.Mock;
using OutOfProcModel.Abstractions.Worker;

namespace OutOfProcModel.Workers;

internal class DefaultWorkerResolver(IWorkerManager workerManager) : IWorkerResolver
{
    private int _lastWorkerIndex = -1;
    private readonly object _lock = new();

    public IWorker ResolveWorker(string context)
    {
        var workers = workerManager.GetWorkers()
            .Where(w => w.Status == WorkerStatus.Running)
            .ToList();

        if (workers.Count == 0)
        {
            return null;
        }

        lock (_lock)
        {
            _lastWorkerIndex = (_lastWorkerIndex + 1) % workers.Count;
            return workers.ElementAt(_lastWorkerIndex);
        }
    }
}

internal class PlaceholderWorkerResolver(IWorkerManager workerManager) : IWorkerResolver
{
    private readonly IWorker _worker = new AggregateWorker(workerManager);

    public IWorker ResolveWorker(string context) => _worker;

    private class AggregateWorker(IWorkerManager workerManager) : IWorker
    {
        private readonly IWorkerManager _workerManager = workerManager;
        private static WorkerStack _runtimeEnvironment = new("_any", "0.0.0", "_any", true);

        public WorkerStatus Status => throw new NotImplementedException("This worker does not have a specific status.");

        public WorkerDefinition Definition { get; } = new WorkerDefinition("ph_aggregate", new ApplicationDefinition("ph_id", "1.0.0"), [], _runtimeEnvironment);

        public IExternalWorkerChannel Channel => throw new NotImplementedException();

        // This will never happen in this flow
        public Task DrainAsync(TimeSpan timeout) => throw new NotImplementedException("This worker does not support draining.");

        public async ValueTask<ScriptInvocationResult> ProcessEvent(ScriptInvocationContext context)
        {
            List<Task<ScriptInvocationResult>> tasks = [];

            foreach (var worker in _workerManager.GetWorkers())
            {
                if (worker.Status == WorkerStatus.Running)
                {
                    tasks.Add(worker.InvokeAsync(context));
                }
            }

            await Task.WhenAll(tasks);

            // merge into a single result
            return new ScriptInvocationResult(); // context.InvocationId, $"[{string.Join("; ", tasks.Select(t => t.Result.Result))}]");
        }

        public Task<ScriptInvocationResult> InvokeAsync(ScriptInvocationContext context)
        {
            throw new NotImplementedException();
        }

        public Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync()
        {
            throw new NotImplementedException();
        }
    }
}