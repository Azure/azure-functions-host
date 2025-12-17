namespace OutOfProcModel.Abstractions.Worker;

internal interface IWorkerResolver
{
    IWorker? ResolveWorker(string context);
}