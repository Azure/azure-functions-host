// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class CompositeArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly IEnumerable<IBlobArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(IEnumerable<IBlobArgumentBindingProvider> providers)
        {
            _providers = providers;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            foreach (IBlobArgumentBindingProvider provider in _providers)
            {
                IBlobArgumentBinding binding = provider.TryCreate(parameter, access);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
