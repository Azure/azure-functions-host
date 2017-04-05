// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class AssemblyExtensions
    {
        private static readonly ConcurrentDictionary<Assembly, string> _codeBaseMapping = new ConcurrentDictionary<Assembly, string>();

        public static bool MapCodeBase(this Assembly assembly, string codeBasePath)
        {
            var uri = new Uri(codeBasePath);
            return _codeBaseMapping.TryAdd(assembly, uri.AbsoluteUri);
        }

        public static string GetCodeBase(this Assembly assembly)
        {
            string codeBase;
            if (_codeBaseMapping.TryGetValue(assembly, out codeBase))
            {
                return codeBase;
            }

            return assembly.CodeBase;
        }
    }
}
