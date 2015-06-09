// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class CompositeArgumentBindingProvider : ITableArgumentBindingProvider
    {
        private readonly IEnumerable<ITableArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(params ITableArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public ITableArgumentBinding TryCreate(ParameterInfo parameter)
        {
            foreach (ITableArgumentBindingProvider provider in _providers)
            {
                ITableArgumentBinding binding = provider.TryCreate(parameter);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
