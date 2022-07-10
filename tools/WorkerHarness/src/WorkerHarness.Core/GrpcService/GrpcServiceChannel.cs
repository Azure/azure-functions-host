// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Grpc.Core;
using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Threading.Channels;

namespace WorkerHarness.Core
{
    public class GrpcServiceChannel
    {
        public GrpcServiceChannel(Channel<StreamingMessage> inboundChannel, Channel<StreamingMessage> outboundChannel)
        {
            InboundChannel = inboundChannel ?? throw new ArgumentNullException(nameof(inboundChannel));
            OutboundChannel = outboundChannel ?? throw new ArgumentNullException(nameof(outboundChannel));
        }

        public Channel<StreamingMessage> InboundChannel { get; }

        public Channel<StreamingMessage> OutboundChannel { get; }
    }
}