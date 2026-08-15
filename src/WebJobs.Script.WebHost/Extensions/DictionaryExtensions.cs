// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal static class DictionaryExtensions
    {
        /// <summary>
        /// Converts a sequence to a <see cref="Dictionary{TKey, TValue}"/> using the specified
        /// <paramref name="comparer"/>, keeping the first value when duplicate keys are encountered
        /// instead of throwing <see cref="ArgumentException"/>.
        /// </summary>
        public static Dictionary<TKey, TValue> ToDictionarySafe<TSource, TKey, TValue>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TValue> valueSelector,
            IEqualityComparer<TKey> comparer,
            ILogger? logger)
            where TKey : notnull
        {
            var dictionary = new Dictionary<TKey, TValue>(comparer);

            foreach (TSource item in source)
            {
                TKey key = keySelector(item);
                if (!dictionary.TryAdd(key, valueSelector(item)))
                {
                    logger?.LogWarning("Duplicate key '{keyName}' encountered when building secret cache; keeping the first value.", key);
                }
            }

            return dictionary;
        }
    }
}
