namespace Microsoft.WindowsAzure.Jobs
{
    // This function was executed by a new blob was written. 
    // Corresponds to [BlobInput]
    internal class BlobTriggerReason : TriggerReason
    {
        public CloudBlobPath BlobPath { get; set; }

        public override string ToString()
        {
            return "New blob input detected: " + BlobPath.ToString();
        }
    }
}
