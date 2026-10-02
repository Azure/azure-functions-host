// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Workers;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Dispatches function invocations through client-backed worker channels.
/// </summary>
public interface IRpcClientFunctionInvocationDispatcher : IFunctionInvocationDispatcher
{
    /// <summary>
    /// Sets up invocation buffers and sends function load requests to a newly linked channel.
    /// </summary>
    /// <remarks>
    /// Each channel is configured at most once per dispatcher. A synchronous setup failure is not retried and
    /// prevents this dispatcher from selecting that channel for invocations. Function-load responses arrive separately.
    /// </remarks>
    /// <param name="channel">The newly linked channel.</param>
    /// <returns>A task that completes after buffers are initialized and function-load requests are sent.</returns>
    Task SetupChannelAsync(WorkerChannel channel);
}
