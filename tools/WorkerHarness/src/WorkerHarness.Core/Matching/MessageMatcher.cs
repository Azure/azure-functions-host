// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using WorkerHarness.Core.Actions;

namespace WorkerHarness.Core.Matching
{
    public class MessageMatcher : IMessageMatcher
    {
        private readonly IContextMatcher _contextMatcher;

        public MessageMatcher(IContextMatcher contextMatcher)
        {
            _contextMatcher = contextMatcher;
        }

        public bool Match(RpcActionMessage rpcActionMessage, StreamingMessage streamingMessage)
        {
            // check if the message type matches
            if (!string.Equals(streamingMessage.ContentCase.ToString(), rpcActionMessage.MessageType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // match streamingMessage against the MatchingContexts in rpcActionMessage
            return _contextMatcher.MatchAll(rpcActionMessage.MatchingCriteria, streamingMessage);   
        }
    }
}
