namespace OutOfProcModel.Abstractions.Worker;

// JobHost-scoped. 
// Retrieved by WebHost-level Grpc server when a new connection is made.
internal interface IWorkerManager
{
    ValueTask<IWorkerState> CreateWorkerAsync(WorkerCreationContext worker);

    /// <summary>
    /// Removes the worker from load balancing, then Drains, Stops, and disposes it.
    /// </summary>
    /// <param name="workerId">The worker to remove.</param>    
    /// <returns>true if successful.</returns>
    Task<bool> RemoveWorkerAsync(string workerId);

    IReadOnlyCollection<IWorker> GetWorkers();
}