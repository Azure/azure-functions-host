// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class CompositeEntityArgumentBindingProvider : ITableEntityArgumentBindingProvider
    {
        private readonly IEnumerable<ITableEntityArgumentBindingProvider> _providers;

        public CompositeEntityArgumentBindingProvider(params ITableEntityArgumentBindingProvider[] providers)
        {
            _providers = providers;
        }

        public IArgumentBinding<TableEntityContext> TryCreate(Type parameterType)
        {
            foreach (ITableEntityArgumentBindingProvider provider in _providers)
            {
                IArgumentBinding<TableEntityContext> binding = provider.TryCreate(parameterType);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }
    }
}
