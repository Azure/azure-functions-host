namespace Microsoft.WindowsAzure.Jobs
{
    // Submit a function for execution.
    // This includes queuing the function as well as updating all associated logging. 
    internal interface IQueueFunction
    {
        // If instance has prereqs, this may result in a call to IPrereqTable.AddPrereq, 
        // which may result in a callback to ActivateFunction when the prereqs are satisfied. 
        ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance);
    }
}
