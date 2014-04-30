namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class FunctionStartedMessage : PersistentQueueMessage
    {
        public ExecutionInstanceLogEntity LogEntity { get; set; }
    }
}
