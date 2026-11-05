// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Publishes current runtime capacity before operations that retire HTTP workers.
/// </summary>
public interface IAppServerHostStatePublisher
{
    /// <summary>
    /// Refreshes runtime state and waits for a successful outbound AppServer publication.
    /// </summary>
    /// <param name="cancellationToken">Cancels publication and existing transient-failure retries.</param>
    /// <returns>A task completed only after AppServer returns a successful HTTP status.</returns>
    /// <remarks>A successful response does not constitute an applied-state acknowledgement from AppServer.</remarks>
    Task PublishCurrentAsync(CancellationToken cancellationToken);
}
