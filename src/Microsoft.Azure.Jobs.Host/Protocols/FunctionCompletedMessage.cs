namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class FunctionCompletedMessage : PersistentQueueMessage
    {
        public ExecutionInstanceLogEntity LogEntity { get; set; }
    }
}
