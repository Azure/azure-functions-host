// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class IDictionaryExtensions
    {
        public static void AddRange<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, IReadOnlyDictionary<TKey, TValue> other)
        {
            if (other == null)
            {
                return;
            }

            foreach (var pair in other)
            {
                dictionary[pair.Key] = pair.Value;
            }
        }

        public static bool TryGetValue<TValue>(this IDictionary<string, object> obj, string name, out TValue value, bool ignoreCase = false)
        {
            value = default(TValue);

            // first try a default case sensitive match (dictionary might contain duplicate
            // keys varying by case only)
            object tempValue = null;
            bool found = obj.TryGetValue(name, out tempValue);

            if (!found && ignoreCase)
            {
                // try a case insensitive match - first match wins
                // for small dictionaries the linear scan won't be an issue
                string key = obj.Keys.FirstOrDefault(p => string.Compare(p, name, StringComparison.OrdinalIgnoreCase) == 0);
                if (key != null)
                {
                    tempValue = obj[key];
                    found = true;
                }
            }

            if (found && (tempValue is TValue || tempValue == null))
            {
                value = (TValue)tempValue;
                return true;
            }

            return false;
        }

        public static async Task<IDictionary<TKey, TValue>> ToDictionaryAsync<TInput, TKey, TValue>(this IEnumerable<TInput> enumerable, Func<TInput, TKey> syncKeySelector, Func<TInput, Task<TValue>> asyncValueSelector)
        {
            IEnumerable<Task<KeyValuePair<TKey, TValue>>> tasks = enumerable.Select(async input => new KeyValuePair<TKey, TValue>(syncKeySelector(input), await asyncValueSelector(input)));
            KeyValuePair<TKey, TValue>[] keyValuePairs = await Task.WhenAll(tasks);
            return keyValuePairs.ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }
}
