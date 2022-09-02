// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Channels;
using Microsoft.Azure.WebJobs.Script.Eventing;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Eventing;

internal static class GrpcEventExtensions
{
    // flow here is:
    // 1) external request is proxied to the the GrpcWorkerChannel via one of the many Send* APIs, which writes
    //    to outbound-writer; this means we can have concurrent writes to outbound
    // 2) if an out-of-process function is connected, a FunctionRpcService-EventStream will consume
    //    from outbound-reader (we'll allow for the multi-stream possibility, hence concurrent), and push it via gRPC
    // 3) when the out-of-process function provides a response to FunctionRpcService-EventStream, it is written to
    //    inbound-writer (note we will allow for multi-stream possibility)
    // 4) the GrpcWorkerChannel has a single dedicated consumer of inbound-reader, which it then marries to
    //    in-flight operations
    internal static readonly UnboundedChannelOptions InboundOptions = new UnboundedChannelOptions
    {
        SingleReader = true, // see 4
        SingleWriter = false, // see 3
        AllowSynchronousContinuations = false,
    };

    internal static readonly UnboundedChannelOptions OutboundOptions = new UnboundedChannelOptions
    {
        SingleReader = false, // see 2
        SingleWriter = false, // see 1
        AllowSynchronousContinuations = false,
    };

    public static void AddGrpcChannels(this IScriptEventManager manager, string workerId)
    {
        var inbound = Channel.CreateUnbounded<InboundGrpcEvent>(InboundOptions);
        if (manager.TryAddWorkerState(workerId, inbound))
        {
            var outbound = Channel.CreateUnbounded<OutboundGrpcEvent>(OutboundOptions);
            if (manager.TryAddWorkerState(workerId, outbound))
            {
                return; // successfully added both
            }
            // we added the inbound but not the outbound; revert
            manager.TryRemoveWorkerState(workerId, out inbound);
        }
         // this is not anticipated, so don't panic abount the allocs above
        throw new ArgumentException("Duplicate worker id: " + workerId, nameof(workerId));
    }

    public static bool TryGetGrpcChannels(this IScriptEventManager manager, string workerId, out Channel<InboundGrpcEvent> inbound, out Channel<OutboundGrpcEvent> outbound)
        => manager.TryGetWorkerState(workerId, out inbound) & manager.TryGetWorkerState(workerId, out outbound);

    public static void RemoveGrpcChannels(this IScriptEventManager manager, string workerId)
    {
        // remove any channels, and shut them down
        if (manager.TryGetWorkerState<Channel<InboundGrpcEvent>>(workerId, out var inbound))
        {
            inbound.Writer.TryComplete();
        }
        if (manager.TryGetWorkerState<Channel<OutboundGrpcEvent>>(workerId, out var outbound))
        {
            outbound.Writer.TryComplete();
        }
    }
}
