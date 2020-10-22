// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Extensions
{
    internal static class DictionaryExtensions
    {
        public static async Task<IDictionary<TKey, TValue>> ToDictionaryAsync<TInput, TKey, TValue>(this IEnumerable<TInput> enumerable, Func<TInput, TKey> syncKeySelector, Func<TInput, Task<TValue>> asyncValueSelector)
        {
            KeyValuePair<TKey, TValue>[] keyValuePairs = await Task.WhenAll(enumerable.Select(async input => new KeyValuePair<TKey, TValue>(syncKeySelector(input), await asyncValueSelector(input))));
            return keyValuePairs.ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }
}
