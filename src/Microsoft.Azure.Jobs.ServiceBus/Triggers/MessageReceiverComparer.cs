using System.Collections.Generic;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs
{
    internal class MessageReceiverComparer : IEqualityComparer<MessageReceiver>
    {
        public bool Equals(MessageReceiver x, MessageReceiver y)
        {
            return x.Path == y.Path;
        }

        public int GetHashCode(MessageReceiver obj)
        {
            return obj.Path.GetHashCode();
        }
    }
}