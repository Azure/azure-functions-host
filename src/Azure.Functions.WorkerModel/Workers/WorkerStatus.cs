namespace OutOfProcModel.Abstractions.Worker;

internal enum WorkerStatus
{
    Created = 0,
    Initializing = 1,
    Initialized = 2,
    Running = 3,
    Draining = 4,
    Drained = 5,
    Stopping = 6,
    Stopped = 7
}