namespace Microsoft.WindowsAzure.Jobs
{
    // This function was executed because an AzureQueue Message
    // Corresponds to [QueueInput].
    internal class QueueMessageTriggerReason : TriggerReason
    {
        public string MessageId { get; set; }

        public override string ToString()
        {
            return "New queue input message on queue";
        }
    }
}
