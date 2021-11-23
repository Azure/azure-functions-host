// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Logging
{
    internal static class IExternalScopeProviderExtensions
    {
        public static IDictionary<string, object> GetScopeDictionaryOrNull(this IExternalScopeProvider scopeProvider)
        {
            IDictionary<string, object> result = null;

            scopeProvider.ForEachScope((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> kvps)
                {
                    result = result ?? new Dictionary<string, object>(16, StringComparer.OrdinalIgnoreCase);

                    foreach (var kvp in kvps)
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                }
            }, (object)null);

            return result;
        }

        /// <summary>
        /// Gets a scope entry by iterating the existing scope, but without allocating a dictionary.
        /// </summary>
        public static object GetScopeEntry(this IExternalScopeProvider scopeProvider, string key)
        {
            // Track the result because we want to _last_ (most local scope), but can only iterate forward
            object result = null;

            scopeProvider.ForEachScope((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> kvps)
                {
                    foreach (var kvp in kvps)
                    {
                        if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                        {
                            result = kvp.Value;
                        }
                    }
                }
            }, (object)null);

            return result;
        }
    }
}
