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
            var result = new DictionaryRef();

            scopeProvider.ForEachScope(static (scope, state) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> kvps)
                {
                    state.Dictionary ??= new Dictionary<string, object>(16, StringComparer.OrdinalIgnoreCase);

                    foreach (var kvp in kvps)
                    {
                        state.Dictionary[kvp.Key] = kvp.Value;
                    }
                }
            }, result);

            return result.Dictionary;
        }

        private class DictionaryRef
        {
            public Dictionary<string, object> Dictionary { get; set; }
        }
    }
}
