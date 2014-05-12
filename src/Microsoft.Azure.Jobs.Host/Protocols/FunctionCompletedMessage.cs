namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class FunctionCompletedMessage : PersistentQueueMessage
    {
        public FunctionCompletedSnapshot Snapshot { get; set; }
    }
}
