// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Grpc.Core;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Threading.Channels;

namespace WorkerHarness.Core
{
    public class GrpcServiceChannel
    {
        public GrpcServiceChannel(Channel<StreamingMessage> channel1, Channel<StreamingMessage> channel2)
        {
            InboundChannel = channel1 ?? throw new ArgumentNullException(nameof(channel1));
            OutboundChannel = channel2 ?? throw new ArgumentNullException(nameof(channel2));
        }

        public Channel<StreamingMessage> InboundChannel { get; }

        public Channel<StreamingMessage> OutboundChannel { get; }
    }
}