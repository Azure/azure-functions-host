// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.Rpc.Client;

internal sealed class RpcClientConnectionOptions
{
    internal RpcClientConnectionOptions(Uri endpoint, string workerId)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!endpoint.IsAbsoluteUri ||
            (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw new ArgumentException("The endpoint must be an absolute HTTP or HTTPS URI with a host.", nameof(endpoint));
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.Equals(endpoint.AbsolutePath, "/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "The endpoint must not include user information, a path, a query, or a fragment.", nameof(endpoint));
        }

        if (string.IsNullOrWhiteSpace(workerId))
        {
            throw new ArgumentException("The worker ID cannot be empty.", nameof(workerId));
        }

        Endpoint = endpoint;
        WorkerId = workerId;
    }

    internal Uri Endpoint { get; }

    internal string WorkerId { get; }
}
