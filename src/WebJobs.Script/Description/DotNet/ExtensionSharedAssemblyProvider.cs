// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
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
        private readonly Collection<ScriptBindingProvider> _bindingProviders;

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="bindingProviders">The collection of <see cref="ScriptBindingProvider"/>s.</param>
        public ExtensionSharedAssemblyProvider(Collection<ScriptBindingProvider> bindingProviders)
        {
            _bindingProviders = bindingProviders;
        }

        public bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            assembly = null;

            foreach (var bindingProvider in _bindingProviders)
            {
                if (bindingProvider.TryResolveAssembly(assemblyName, out assembly))
                {
                    break;
                }
            }

            return assembly != null;
        }
    }
}
