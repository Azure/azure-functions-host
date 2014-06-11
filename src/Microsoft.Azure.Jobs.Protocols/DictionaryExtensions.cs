using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Protocols
{
    internal static class DictionaryExtensions
    {
        public static void RemoveIfContainsKey<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException("dictionary");
            }

            if (dictionary.ContainsKey(key))
            {
                dictionary.Remove(key);
            }
        }
    }
}
