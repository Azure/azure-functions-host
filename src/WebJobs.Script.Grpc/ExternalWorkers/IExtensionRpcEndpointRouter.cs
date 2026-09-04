// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Resolves ASP.NET Core extension gRPC endpoints for connected workers.
/// </summary>
internal interface IExtensionRpcEndpointRouter
{
    /// <summary>
    /// Acquires the endpoint registered for a worker and gRPC method.
    /// </summary>
    /// <param name="workerId">The connected worker identifier.</param>
    /// <param name="method">The fully qualified gRPC method path.</param>
    /// <param name="cancellationToken">A token that cancels endpoint acquisition.</param>
    /// <returns>The acquired endpoint, or <see langword="null"/> when no endpoint is available.</returns>
    ValueTask<ExtensionRpcEndpoint?> RouteAsync(string workerId, string method, CancellationToken cancellationToken);
}
