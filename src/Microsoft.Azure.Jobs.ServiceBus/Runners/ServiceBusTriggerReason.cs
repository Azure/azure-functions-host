using System;
using System.Globalization;

namespace Microsoft.Azure.Jobs
{
    // This function was executed because an ServiceBus Message
    // Corresponds to [ServiceBusInput].
    internal class ServiceBusTriggerReason : TriggerReason
    {
        public string MessageId { get; set; }

        // Path of the Queue or Subscription
        public string EntityPath { get; set; }

        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "New service bus input message on '{0}'", EntityPath);
        }
    }
}