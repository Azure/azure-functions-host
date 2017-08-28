using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Eventing.Rpc
{
    public class OutboundEvent : RpcEvent
    {
        public OutboundEvent(string workerId, StreamingMessage message) : base(workerId, message, MessageOrigin.Host)
        {
        }
    }
}
