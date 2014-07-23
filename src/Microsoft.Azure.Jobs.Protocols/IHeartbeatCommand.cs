// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Defines a command that signals a heartbeat from a running host instance.</summary>
#if PUBLICPROTOCOL
    public interface IHeartbeatCommand
#else
    internal interface IHeartbeatCommand
#endif
    {
        /// <summary>Signals a heartbeat from a running host instance.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will signal the heartbeat.</returns>
        Task BeatAsync(CancellationToken cancellationToken);
    }
}
