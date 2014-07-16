// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class BindingSource : IBindingSource
    {
        private readonly IFunctionBinding _binding;
        private readonly IDictionary<string, object> _parameters;

        public BindingSource(IFunctionBinding binding, IDictionary<string, object> parameters)
        {
            _binding = binding;
            _parameters = parameters;
        }

        public IReadOnlyDictionary<string, IValueProvider> Bind(FunctionBindingContext context)
        {
            return _binding.Bind(context, _parameters);
        }
    }
}
