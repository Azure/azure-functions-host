// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace Azure.Functions.WorkerProxy.Http;

internal static class WorkerHttpProxyEndpointResolver
{
    public static bool TryResolve(string authority, string scheme, [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(authority)
            || !string.Equals(authority, authority.Trim(), StringComparison.Ordinal)
            || authority.IndexOfAny(['/', '?', '#', '@']) >= 0
            || !TryGetExplicitPort(authority)
            || (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || !Uri.TryCreate($"{scheme}://{authority}", UriKind.Absolute, out Uri? parsed)
            || !string.Equals(parsed.Scheme, scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parsed.AbsolutePath, "/", StringComparison.Ordinal)
            || parsed.UserInfo.Length != 0
            || parsed.Query.Length != 0
            || parsed.Fragment.Length != 0
            || parsed.Port == 0
            || (IPAddress.TryParse(parsed.Host, out IPAddress? address) && address.Equals(IPAddress.Any)))
        {
            return false;
        }

        endpoint = parsed;
        return true;
    }

    private static bool TryGetExplicitPort(string authority)
    {
        int separator = authority.LastIndexOf(':');
        if (separator <= 0 || authority.IndexOf(':') != separator)
        {
            return false;
        }

        return int.TryParse(authority.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            && port is > 0 and <= 65535;
    }
}
