namespace Microsoft.Azure.Functions.WorkerModel.Grpc;

internal enum StreamState
{
    None,
    Connected,
    Initialized,
    RunningAsPlaceholder,
    Specializing,
    Running,
    Draining,
    Stopped
}

internal enum WorkerAction
{
    StartStream,
    WorkerConnect,
    WorkerInitResponse,
    MetadataResponse,
    InvocationResponse,
    Specialize,
    EnvironmentReloadResponse,
}
