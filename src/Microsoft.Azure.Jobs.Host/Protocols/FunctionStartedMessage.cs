namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class FunctionStartedMessage : PersistentQueueMessage
    {
        public FunctionStartedSnapshot Snapshot { get; set; }
    }
}
