namespace OutOfProcModel.Abstractions.Worker;

internal interface IWorkerFactory
{
    ValueTask<IWorker> Create(WorkerCreationContext context);
}