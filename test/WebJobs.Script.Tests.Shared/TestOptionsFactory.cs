// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestOptionsFactory<T> : IOptionsFactory<T>, IOptionsMonitorCache<T> where T : class, new()
    {
        private readonly T _options;
        private readonly Dictionary<string, T> _cache = new Dictionary<string, T>();

        public TestOptionsFactory(T options)
        {
            _options = options;
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public T Create(string name)
        {
            return _options;
        }

        public T GetOrAdd(string name, Func<T> createOptions)
        {
            return _options;
        }

        public bool TryAdd(string name, T options)
        {
            return _cache.TryAdd(name, options);
        }

        public bool TryRemove(string name)
        {
            if (!_cache.ContainsKey(name))
            {
                return false;
            }

            return _cache.Remove(name);
        }
    }
}
