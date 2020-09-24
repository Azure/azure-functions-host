// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using static Microsoft.Azure.WebJobs.Script.Grpc.Messages.RpcLog.Types;
using MessageType = Microsoft.Azure.WebJobs.Script.Grpc.Messages.StreamingMessage.ContentOneofCase;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public static class InboundGrpcEventExtensions
    {
        public static bool IsMessageOfType(this InboundGrpcEvent inboundEvent, MessageType typeToCheck)
        {
            return inboundEvent.MessageType.Equals(typeToCheck);
        }

        public static bool IsLogOfCategory(this InboundGrpcEvent inboundEvent, RpcLogCategory categoryToCheck)
        {
            return inboundEvent.Message.RpcLog.LogCategory.Equals(categoryToCheck);
        }
    }
}
