namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal class FunctionCompletedMessage : PersistentQueueMessage
    {
        public ExecutionInstanceLogEntity LogEntity { get; set; }
    }
}
