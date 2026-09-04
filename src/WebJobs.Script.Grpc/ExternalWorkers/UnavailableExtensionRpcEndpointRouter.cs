// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Provides a router that reports no extension endpoints when extensibility is unavailable.
/// </summary>
internal sealed class UnavailableExtensionRpcEndpointRouter : IExtensionRpcEndpointRouter
{
    /// <inheritdoc/>
    public ValueTask<ExtensionRpcEndpoint?> RouteAsync(
        string workerId,
        string method,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<ExtensionRpcEndpoint?>(null);
    }
}
