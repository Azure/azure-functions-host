// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public sealed class RpcMessageReceivedEventArgs : EventArgs
    {
        public StreamingMessage Message { get; set; }

        public IAsyncStreamReader<StreamingMessage> Reader { get; set; }

        public IServerStreamWriter<StreamingMessage> Writer { get; set; }
    }
}
