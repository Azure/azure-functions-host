// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    /// <summary>
    /// This binding provider loads any <see cref="IArgumentBindingProvider{ITableArgumentBinding}"/> instances
    /// registered with the <see cref="IExtensionRegistry"/>. When it binds, it delegates to those
    /// providers.
    /// </summary>
    internal class TableArgumentBindingExtensionProvider : IStorageTableArgumentBindingProvider
    {
        private IEnumerable<IArgumentBindingProvider<ITableArgumentBinding>> _bindingExtensionsProviders;

        public TableArgumentBindingExtensionProvider(IExtensionRegistry extensions)
        {
            _bindingExtensionsProviders = extensions.GetExtensions<IArgumentBindingProvider<ITableArgumentBinding>>();
        }

        public IStorageTableArgumentBinding TryCreate(ParameterInfo parameter)
        {
            // see if there are any registered binding extension providers that can
            // bind to this parameter
            foreach (IArgumentBindingProvider<ITableArgumentBinding> provider in _bindingExtensionsProviders)
            {
                ITableArgumentBinding bindingExtension = provider.TryCreate(parameter);
                if (bindingExtension != null)
                {
                    // if an extension is able to bind, wrap the binding
                    return new TableArgumentBindingExtension(bindingExtension);
                }
            }

            return null;
        }

        /// <summary>
        /// This binding wraps the actual extension binding and delegates to it. It exists to map
        /// from from the internal <see cref="IStorageTableArgumentBinding"/> interface into the
        /// public ITableArgumentBinding interface.
        /// </summary>
        internal class TableArgumentBindingExtension : IStorageTableArgumentBinding
        {
            private ITableArgumentBinding _binding;

            public TableArgumentBindingExtension(ITableArgumentBinding binding)
            {
                _binding = binding;
            }

            public FileAccess Access
            {
                get { return _binding.Access; }
            }

            public Type ValueType
            {
                get { return _binding.ValueType; }
            }

            public Task<IValueProvider> BindAsync(IStorageTable value, ValueBindingContext context)
            {
                CloudTable table = null;
                if (value != null)
                {
                    table = value.SdkObject;
                }
                return _binding.BindAsync(table, context);
            }
        }
    }
}
