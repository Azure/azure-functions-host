// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Observes HTTP capacity acknowledged by AppServer through the hosted state publisher.
/// </summary>
public interface IAppServerHttpCapacityReadiness
{
    /// <summary>
    /// Waits until AppServer has acknowledged the current, positive HTTP capacity snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancels only this wait.</param>
    /// <returns>A task completing when the current positive snapshot has been acknowledged.</returns>
    /// <remarks>
    /// This is a point-in-time handoff, not a reservation or a guarantee of future capacity.
    /// Slot admission remains authoritative.
    /// </remarks>
    Task WaitForAcknowledgedHttpCapacityAsync(CancellationToken cancellationToken = default);
}
