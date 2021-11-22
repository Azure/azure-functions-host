// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;

namespace Microsoft.Azure.WebJobs.Script
{
    public class SystemEnvironment : IEnvironment
    {
        private static readonly ConcurrentDictionary<string, string> _cache = new ConcurrentDictionary<string, string>();

        private SystemEnvironment()
        {
        }

        public static SystemEnvironment Instance => new SystemEnvironment();

        public string GetEnvironmentVariable(string name)
        {
            return _cache.GetOrAdd(name, static n => Environment.GetEnvironmentVariable(n));
        }

        public void SetEnvironmentVariable(string name, string value)
        {
            _cache[name] = value;
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
