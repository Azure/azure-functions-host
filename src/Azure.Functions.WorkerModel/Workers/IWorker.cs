using System.Collections.Immutable;
using Microsoft.Azure.WebJobs.Script.Description;
using OutOfProcModel.Abstractions.Mock;

namespace OutOfProcModel.Abstractions.Worker;

internal interface IWorker : IWorkerState
{
    // would messages from grpc call this also?
    Task<ScriptInvocationResult> InvokeAsync(ScriptInvocationContext context);

    Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync();

    // Returns when all in-flight invocations have completed (or timeout is hit)
    Task DrainAsync(TimeSpan timeout);
}

// Stuff the WebHost can interact with
internal interface IWorkerState
{
    WorkerDefinition Definition { get; }

    WorkerStatus Status { get; }

    IExternalWorkerChannel Channel { get; }
}