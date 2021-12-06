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
    }
}
