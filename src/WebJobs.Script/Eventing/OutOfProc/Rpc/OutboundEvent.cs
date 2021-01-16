// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
