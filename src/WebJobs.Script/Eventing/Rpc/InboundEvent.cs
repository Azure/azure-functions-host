// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Eventing.Rpc
{
    public class InboundEvent : RpcEvent
    {
        public InboundEvent(string workerId, StreamingMessage message) : base(workerId, message, MessageOrigin.Worker)
        {
        }
    }
}
