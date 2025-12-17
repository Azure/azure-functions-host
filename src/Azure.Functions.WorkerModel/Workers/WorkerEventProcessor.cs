using OutOfProcModel.Abstractions.Core;
using OutOfProcModel.Abstractions.Worker;

namespace OutOfProcModel.Workers;

internal class WorkerEventProcessor(IWorkerResolver workerResolver) : IEventProcessor
{
    private readonly IWorkerResolver _workerResolver = workerResolver;

    public async ValueTask<EventResult> ProcessEvent(EventContext context)
    {
        var worker = _workerResolver.ResolveWorker(context.ApplicationId);

        if (worker == null)
        {
            throw new InvalidOperationException("No worker available");
        }

        // what is T going to be here? 
        // Should it be some kind of "Result" object? What if we go to process it, but it's suddenly 
        // now draining... (a race between resolving and processing) should we resolve another worker and try again?
        // var result = await worker.InvokeAsync(context);

        return new EventResult(new InvocationResult(string.Empty, string.Empty), worker.Definition.WorkerId);
    }
}