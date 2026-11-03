// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Handles bounded HTTP requests received from a WorkerProxy over an authorized runtime-owned transport.
/// </summary>
/// <remarks>The implementation owns destination selection and credentials, not the customer request.</remarks>
public interface ITokenServiceProxyRequestHandler
{
    /// <summary>
    /// Sends an opaque request to the configured upstream service.
    /// </summary>
    /// <param name="request">The request without customer-side service credentials.</param>
    /// <param name="limits">The negotiated limits for this transport.</param>
    /// <param name="cancellationToken">Cancels this request, not the shared transport.</param>
    /// <returns>The upstream HTTP status, end-to-end headers, and body.</returns>
    Task<HttpProxyResponse> SendAsync(HttpProxyRequest request, HttpProxyLimits limits, CancellationToken cancellationToken);
}
