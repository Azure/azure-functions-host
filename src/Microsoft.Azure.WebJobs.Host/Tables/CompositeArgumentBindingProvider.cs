// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class CompositeArgumentBindingProvider : ITableArgumentBindingProvider
    {
        private readonly IEnumerable<ITableArgumentBindingProvider> _providers;

        public CompositeArgumentBindingProvider(params ITableArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public ITableArgumentBinding TryCreate(Type parameterType)
        {
            foreach (ITableArgumentBindingProvider provider in _providers)
            {
                ITableArgumentBinding binding = provider.TryCreate(parameterType);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
