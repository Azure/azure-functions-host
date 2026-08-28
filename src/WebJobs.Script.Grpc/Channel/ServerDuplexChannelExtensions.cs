// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Eventing;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

internal static class ServerDuplexChannelExtensions
{
    internal static ServerDuplexChannel AddServerDuplexChannel(this IScriptEventManager eventManager, string workerId)
    {
        var channel = new ServerDuplexChannel();
        if (!eventManager.TryAddWorkerState(workerId, channel))
        {
            channel.DisposeAsync().GetAwaiter().GetResult();
            throw new ArgumentException("Duplicate worker id: " + workerId, nameof(workerId));
        }

        if (eventManager.TryGetWorkerState(workerId, out ServerDuplexChannel registeredChannel))
        {
            if (!ReferenceEquals(registeredChannel, channel))
            {
                channel.DisposeAsync().GetAwaiter().GetResult();
            }

            return registeredChannel;
        }

        eventManager.TryRemoveWorkerState(workerId, out ServerDuplexChannel removedChannel);
        removedChannel?.DisposeAsync().GetAwaiter().GetResult();
        if (!ReferenceEquals(removedChannel, channel))
        {
            channel.DisposeAsync().GetAwaiter().GetResult();
        }

        throw new InvalidOperationException("Could not retrieve server duplex channel for worker ID: " + workerId);
    }

    internal static bool TryGetServerDuplexChannel(
        this IScriptEventManager eventManager,
        string workerId,
        out ServerDuplexChannel channel)
        => eventManager.TryGetWorkerState(workerId, out channel);
}
