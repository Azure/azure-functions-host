namespace Microsoft.WindowsAzure.Jobs
{
    internal enum FunctionInstanceStatus
    {
        None, // shouldn't be used. Can indicate a serialization error.
        Running, // Started running
        CompletedSuccess, // ran to completion, either via success or a user error (threw exception)
        CompletedFailed, // ran to completion, but function through an exception before finishing
        NeverFinished // Had not finished when host stopped running
    }
}
