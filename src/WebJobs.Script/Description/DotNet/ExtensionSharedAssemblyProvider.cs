// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensibility;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// An <see cref="ISharedAssemblyProvider"/> that delegates to a collection of <see cref="ScriptBindingProvider"/>s
    /// providing them a chance to resolve assemblies for extensions.
    /// </summary>
    public class ExtensionSharedAssemblyProvider : ISharedAssemblyProvider
    {
        private readonly ICollection<ScriptBindingProvider> _bindingProviders;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExtensionSharedAssemblyProvider"/> class.
        /// </summary>
        /// <param name="bindingProviders">The collection of <see cref="ScriptBindingProvider"/>s.</param>
        public ExtensionSharedAssemblyProvider(ICollection<ScriptBindingProvider> bindingProviders)
        {
            _bindingProviders = bindingProviders;
        }

        public bool TryResolveAssembly(string assemblyName, AssemblyLoadContext targetContext, out Assembly assembly)
        {
            assembly = null;

            foreach (var bindingProvider in _bindingProviders)
            {
                if (bindingProvider.TryResolveAssembly(assemblyName, targetContext, out assembly) ||
                    TryResolveExtensionAssembly(bindingProvider, assemblyName, out assembly))
                {
                    break;
                }
            }

            return assembly != null;
        }

        private static bool TryResolveExtensionAssembly(ScriptBindingProvider bindingProvider, string assemblyName, out Assembly assembly)
        {
            assembly = null;
            Assembly providerAssembly = bindingProvider.GetType().Assembly;

            if (string.Compare(assemblyName, AssemblyNameCache.GetName(providerAssembly).Name) == 0)
            {
                assembly = providerAssembly;
            }

            return assembly != null;
        }
    }
}
