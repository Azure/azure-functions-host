// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Eventing.Rpc;
using static Microsoft.Azure.WebJobs.Script.Grpc.Messages.RpcLog.Types;
using MessageType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class InboundEventExtensions
    {
        public static bool IsMessageOfType(this InboundEvent inboundEvent, MessageType typeToCheck)
        {
            return inboundEvent.MessageType.Equals(typeToCheck);
        }

        public static bool IsLogOfCategory(this InboundEvent inboundEvent, RpcLogCategory categoryToCheck)
        {
            return inboundEvent.Message.RpcLog.LogCategory.Equals(categoryToCheck);
        }
    }
}
