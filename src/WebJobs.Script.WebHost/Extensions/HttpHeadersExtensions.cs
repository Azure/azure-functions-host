// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class HttpHeadersExtensions
    {
        public static Dictionary<string, StringValues> ToCoreHeaders(this HttpHeaders headers, params string[] exclude)
        {
            return headers
                .Where(h => !exclude.Any(e => e.Equals(h.Key, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(k => k.Key, v => new StringValues(v.Value.ToArray()));
        }
    }
}