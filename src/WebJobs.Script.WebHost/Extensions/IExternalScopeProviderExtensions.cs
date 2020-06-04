// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Logging
{
    internal static class IExternalScopeProviderExtensions
    {
        public static IDictionary<string, object> GetScopeDictionary(this IExternalScopeProvider scopeProvider)
        {
            var dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            scopeProvider.ForEachScope((scope, d) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> kvps)
                {
                    foreach (var kvp in kvps)
                    {
                        d[kvp.Key] = kvp.Value;
                    }
                }
            }, dictionary);

            return dictionary;
        }
    }
}
