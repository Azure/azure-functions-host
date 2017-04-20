// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script.Extensibility;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// An <see cref="ISharedAssemblyProvider"/> that delegates to a collection of <see cref="ScriptBindingProvider"/>s
    /// providing them a chance to resolve assemblies for extensions.
    /// </summary>
    [CLSCompliant(false)]
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

        public bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;

            foreach (var bindingProvider in _bindingProviders)
            {
                if (bindingProvider.TryResolveAssembly(assemblyName, out assembly) ||
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

            if (string.Compare(assemblyName, providerAssembly.GetName().Name) == 0)
            {
                assembly = providerAssembly;
            }

            return assembly != null;
        }
    }
}
