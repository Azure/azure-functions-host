// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
