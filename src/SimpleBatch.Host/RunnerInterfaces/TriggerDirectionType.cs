namespace Microsoft.WindowsAzure.Jobs
{
    internal enum TriggerDirectionType
    {
        // Parameter is an input that we can reason about and can trigger (eg,  [BlobInput])
        Input,

        // Parameter is an output that we can reason about  (eg [BlobOutput])
        Output,

        // Parameter does not cause triggering. 
        Ignore
    }
}
