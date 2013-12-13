namespace WebFrontEnd.Models.Protocol
{
    public enum FunctionInstanceStatusModel
    {
        None, // shouldn't be used. Can indicate a serialization error.
        AwaitingPrereqs, // function is not yet queued. Has outstanding prereqs. 
        Queued, // waiting in the execution queue.
        Running, // Now running. An execution node has picked up ownership.
        CompletedSuccess, // ran to completion, either via success or a user error (threw exception)
        CompletedFailed, // ran to completion, but function through an exception before finishing
    }
}