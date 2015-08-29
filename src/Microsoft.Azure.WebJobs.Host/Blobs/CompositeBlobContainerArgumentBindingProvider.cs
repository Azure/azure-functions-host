// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class CompositeBlobContainerArgumentBindingProvider : IBlobContainerArgumentBindingProvider
    {
        private readonly IEnumerable<IBlobContainerArgumentBindingProvider> _providers;

        public CompositeBlobContainerArgumentBindingProvider(IEnumerable<IBlobContainerArgumentBindingProvider> providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<IStorageBlobContainer> TryCreate(ParameterInfo parameter)
        {
            foreach (IBlobContainerArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<IStorageBlobContainer> binding = provider.TryCreate(parameter);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
