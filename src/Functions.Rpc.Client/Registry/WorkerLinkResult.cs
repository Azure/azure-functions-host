// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Describes a successful link for one caller after initialization and registration complete.
/// </summary>
/// <param name="Channel">The initialized channel, owned by the registry.</param>
/// <param name="IsNewLink">
/// Whether this caller started the successful attempt. Matching retries return <see langword="false"/>
/// even when they waited for the same pending initialization.
/// </param>
public sealed record WorkerLinkResult(WorkerChannel Channel, bool IsNewLink)
{
    /// <summary>
    /// Gets whether the initialized channel advertised the validated HTTP function-group capability.
    /// </summary>
    public bool IsHttpFunctionGroup { get; init; }

    /// <summary>
    /// Gets a wakeup signal for transport completion, unlink, or registry disposal.
    /// </summary>
    /// <remarks>
    /// This composite can lag its original tasks. Use <see cref="WorkerTransportCompletion"/> and
    /// <see cref="WorkerRemoval"/> directly when deciding whether a link is still live.
    /// The caller does not own or cancel these tasks.
    /// </remarks>
    public Task WorkerCompletion { get; init; } = Task.CompletedTask;

    /// <summary>
    /// Gets the original transport completion task. Any completion means the worker is unavailable.
    /// </summary>
    public Task WorkerTransportCompletion { get; init; } = Task.CompletedTask;

    /// <summary>
    /// Gets the original registry removal signal, completed at unlink or registry disposal before channel cleanup.
    /// </summary>
    public Task WorkerRemoval { get; init; } = Task.CompletedTask;
}
