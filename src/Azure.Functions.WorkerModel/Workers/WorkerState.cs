using Microsoft.Azure.Functions.WorkerModel.JobHost;
using OutOfProcModel.Abstractions.ControlPlane;
using OutOfProcModel.Abstractions.Worker;

namespace OutOfProcModel.Abstractions.Mock;

internal class WorkerState(WorkerDefinition initialDefinition)
{
    public WorkerDefinition Definition { get; } = initialDefinition;

    public WorkerStatus Status { get; set; } = WorkerStatus.Created;

    public WorkerState Specialize(ApplicationDefinition application, Dictionary<string, string> capabilities)
    {
        return new WorkerState(Definition.Specialize(application, capabilities))
        {
            Status = Status
        };
    }
}

internal record WorkerDefinition(
    string WorkerId,
    ApplicationDefinition Application,
    Dictionary<string, string> Capabilities,
    WorkerStack Stack)
{
    public WorkerDefinition Specialize(ApplicationDefinition application, Dictionary<string, string> capabilities)
    {
        if (!Stack.IsPlaceholder)
        {
            throw new InvalidOperationException("Cannot specialize a non-placeholder worker definition.");
        }

        // Clone the current definition with only updates to relevant propserties
        var newRuntimeEnvironment = Stack with
        {
            IsPlaceholder = false,
        };

        return new WorkerDefinition(
            WorkerId: WorkerId,
            Application: application,
            Capabilities: capabilities,
            Stack: newRuntimeEnvironment);
    }
}