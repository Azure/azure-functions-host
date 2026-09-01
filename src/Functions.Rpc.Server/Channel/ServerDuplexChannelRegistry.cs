// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc;

internal class ServerDuplexChannelRegistry
{
    private readonly ConcurrentDictionary<string, Lease> _leases = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates an exclusive channel lease whose ownership transfers to the caller.
    /// </summary>
    /// <param name="workerId">The worker identifier used to resolve the service endpoints.</param>
    /// <returns>
    /// The channel lease. Disposing it releases the worker registration and disposes the underlying server channel.
    /// </returns>
    internal DuplexChannel<StreamingMessage> CreateLease(string workerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(workerId);

        ServerDuplexChannel channel = CreateChannel();
        var lease = new Lease(this, workerId, channel);
        if (!_leases.TryAdd(workerId, lease))
        {
            lease.DisposeAsync().GetAwaiter().GetResult();
            throw new ArgumentException("Duplicate worker id: " + workerId, nameof(workerId));
        }

        return lease;
    }

    /// <summary>
    /// Tries to get the borrowed endpoints used by <c>FunctionRpcService</c>.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="endpoints">The borrowed service endpoints when a lease exists.</param>
    /// <returns><see langword="true"/> when the worker has an active lease; otherwise, <see langword="false"/>.</returns>
    internal bool TryGetServiceEndpoints(string workerId, out FunctionRpcChannelEndpoints endpoints)
    {
        if (_leases.TryGetValue(workerId, out Lease lease))
        {
            endpoints = lease.ServiceEndpoints;
            return true;
        }

        endpoints = default;
        return false;
    }

    /// <summary>
    /// Creates the underlying server channel.
    /// </summary>
    /// <returns>The channel owned by the lease.</returns>
    protected virtual ServerDuplexChannel CreateChannel() => new();

    private void Release(string workerId, Lease lease)
    {
        ICollection<KeyValuePair<string, Lease>> leases = _leases;
        leases.Remove(new(workerId, lease));
    }

    private sealed class Lease : DuplexChannel<StreamingMessage>
    {
        private readonly ServerDuplexChannelRegistry _registry;
        private readonly string _workerId;
        private readonly ServerDuplexChannel _ownedChannel;

        internal Lease(ServerDuplexChannelRegistry registry, string workerId, ServerDuplexChannel ownedChannel)
        {
            _registry = registry;
            _workerId = workerId;
            _ownedChannel = ownedChannel;

            Reader = ownedChannel.Reader;
            Writer = ownedChannel.Writer;
        }

        internal FunctionRpcChannelEndpoints ServiceEndpoints => _ownedChannel.ServiceEndpoints;

        protected override ValueTask DisposeAsyncCore()
        {
            _registry.Release(_workerId, this);
            return _ownedChannel.DisposeAsync();
        }
    }
}
