using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class NewQueueMessageRuntimeBindingInputs : RuntimeBindingInputs, ITriggerNewQueueMessage
    {
        public NewQueueMessageRuntimeBindingInputs(FunctionLocation location, CloudQueueMessage queueMessage)
            : base(location)
        {
            this.QueueMessageInput = queueMessage;
        }

        // Non-null if this was triggered by a new azure Q message. 
        public CloudQueueMessage QueueMessageInput { get; private set; }
    }
}
