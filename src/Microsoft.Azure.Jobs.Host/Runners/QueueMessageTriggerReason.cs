using System;
using System.Globalization;

namespace Microsoft.Azure.Jobs
{
    // This function was executed because an AzureQueue Message
    // Corresponds to [QueueInput].
    internal class QueueMessageTriggerReason : TriggerReason
    {
        public string MessageId { get; set; }

        // Name of the queue
        public string QueueName { get; set; }

        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "New queue input message on '{0}'", QueueName);
        }
    }
}
