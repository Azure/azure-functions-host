// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Azure.Functions.Authentication;

namespace Azure.Functions.WorkerProxy.Http;

internal static class WorkerHttpProxyEndpointResolver
{
    public static bool TryResolve(string authority, string scheme, [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;
        if (!string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !RuntimeWorkerAuthentication.TryCreateHttpsOrigin(authority, out Uri? parsed)
            || (IPAddress.TryParse(parsed.Host.Trim('[', ']'), out IPAddress? address)
                && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))))
        {
            return false;
        }

        endpoint = parsed;
        return true;
    }
}
