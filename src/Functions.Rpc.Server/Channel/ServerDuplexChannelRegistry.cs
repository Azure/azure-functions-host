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
    private readonly ConcurrentDictionary<string, Registration> _registrations = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates and registers a channel whose ownership transfers to the caller.
    /// </summary>
    /// <param name="workerId">The worker identifier used to resolve the service endpoints.</param>
    /// <returns>
    /// The registered channel. Disposing it unregisters the worker and disposes the underlying server channel.
    /// </returns>
    internal DuplexChannel<StreamingMessage> CreateRegisteredChannel(string workerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(workerId);

        ServerDuplexChannel channel = CreateChannel();
        var registration = new Registration(this, workerId, channel);
        if (!_registrations.TryAdd(workerId, registration))
        {
            registration.DisposeAsync().GetAwaiter().GetResult();
            throw new ArgumentException("Duplicate worker id: " + workerId, nameof(workerId));
        }

        return registration;
    }

    /// <summary>
    /// Tries to get the borrowed endpoints used by <c>FunctionRpcService</c>.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="endpoints">The borrowed service endpoints when registration exists.</param>
    /// <returns><see langword="true"/> when the worker is registered; otherwise, <see langword="false"/>.</returns>
    internal bool TryGetServiceEndpoints(string workerId, out FunctionRpcChannelEndpoints endpoints)
    {
        if (_registrations.TryGetValue(workerId, out Registration registration))
        {
            endpoints = registration.ServiceEndpoints;
            return true;
        }

        endpoints = default;
        return false;
    }

    /// <summary>
    /// Creates the underlying server channel.
    /// </summary>
    /// <returns>The channel to own through the registration.</returns>
    protected virtual ServerDuplexChannel CreateChannel() => new();

    private void Unregister(string workerId, Registration registration)
    {
        ICollection<KeyValuePair<string, Registration>> registrations = _registrations;
        registrations.Remove(new(workerId, registration));
    }

    private sealed class Registration : DuplexChannel<StreamingMessage>
    {
        private readonly ServerDuplexChannelRegistry _registry;
        private readonly string _workerId;
        private readonly ServerDuplexChannel _ownedChannel;

        internal Registration(ServerDuplexChannelRegistry registry, string workerId, ServerDuplexChannel ownedChannel)
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
            _registry.Unregister(_workerId, this);
            return _ownedChannel.DisposeAsync();
        }
    }
}
