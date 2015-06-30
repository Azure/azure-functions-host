// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class CompositeArgumentBindingProvider : IStorageTableArgumentBindingProvider
    {
        private readonly IEnumerable<IStorageTableArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(params IStorageTableArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public IStorageTableArgumentBinding TryCreate(ParameterInfo parameter)
        {
            foreach (IStorageTableArgumentBindingProvider provider in _providers)
            {
                IStorageTableArgumentBinding binding = provider.TryCreate(parameter);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
