// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class DirectSharedAssemblyProvider : ISharedAssemblyProvider
    {
        private readonly Assembly _assembly;

        public DirectSharedAssemblyProvider(Assembly assembly)
        {
            _assembly = assembly;
        }

        public bool TryResolveAssembly(string assemblyName, AssemblyLoadContext targetContext, ILogger logger, out Assembly assembly)
        {
            if (string.Compare(AssemblyNameCache.GetName(_assembly).Name, assemblyName, StringComparison.OrdinalIgnoreCase) == 0)
            {
                logger.LogInformation($"{nameof(DirectSharedAssemblyProvider)} resolved shared assembly '{assemblyName}'");

                assembly = _assembly;
                return true;
            }

            assembly = null;
            return false;
        }
    }
}
