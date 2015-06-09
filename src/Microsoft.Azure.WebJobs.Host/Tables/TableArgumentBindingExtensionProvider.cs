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
    /// This binding provider loads any <see cref="ITableArgumentBindingExtensionProvider"/> instances
    /// registered with the <see cref="IExtensionRegistry"/>. When it binds, it delegates to those
    /// providers.
    /// </summary>
    internal class TableArgumentBindingExtensionProvider : ITableArgumentBindingProvider
    {
        private IEnumerable<ITableArgumentBindingExtensionProvider> _bindingExtensionsProviders;

        public TableArgumentBindingExtensionProvider(IExtensionRegistry extensions)
        {
            _bindingExtensionsProviders = extensions.GetExtensions<ITableArgumentBindingExtensionProvider>();
        }

        public ITableArgumentBinding TryCreate(ParameterInfo parameter)
        {
            // see if there are any registered binding extension providers that can
            // bind to this parameter type
            foreach (ITableArgumentBindingExtensionProvider provider in _bindingExtensionsProviders)
            {
                ITableArgumentBindingExtension bindingExtension = provider.TryCreate(parameter);
                if (bindingExtension != null)
                {
                    // if an extension is able to bind, wrap the binding
                    return new TableArgumentBindingExtension(bindingExtension);
                }
            }

            return null;
        }

        /// <summary>
        /// This binding wraps the actual extension binding and delegates to it.
        /// It exists solely to convert from our internal IStorageTable type
        /// to CloudTable.
        /// </summary>
        internal class TableArgumentBindingExtension : ITableArgumentBinding
        {
            private ITableArgumentBindingExtension _bindingExtension;

            public TableArgumentBindingExtension(ITableArgumentBindingExtension bindingExtension)
            {
                _bindingExtension = bindingExtension;
            }

            public FileAccess Access
            {
                get { return _bindingExtension.Access; }
            }

            public Type ValueType
            {
                get { return _bindingExtension.ValueType; }
            }

            public Task<IValueProvider> BindAsync(IStorageTable value, ValueBindingContext context)
            {
                CloudTable table = null;
                if (value != null)
                {
                    table = value.SdkObject;
                }
                return _bindingExtension.BindAsync(table, context);
            }
        }
    }
}
