// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using WorkerHarness.Core.Actions;

namespace WorkerHarness.Core.Matching
{
    /// <summary>
    /// An abtraction to match a RpcActionMessage with a StreamingMessage
    /// </summary>
    public interface IMessageMatcher
    {
        bool Match(RpcActionMessage rpcActionMessage, StreamingMessage streamingMessage);
    }
}
