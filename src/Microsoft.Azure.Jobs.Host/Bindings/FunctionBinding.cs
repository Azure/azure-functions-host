// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class FunctionBinding : IFunctionBinding
    {
        private readonly MethodInfo _method;
        private readonly IReadOnlyDictionary<string, IBinding> _bindings;

        public FunctionBinding(MethodInfo method, IReadOnlyDictionary<string, IBinding> bindings)
        {
            _method = method;
            _bindings = bindings;
        }

        public IReadOnlyDictionary<string, IValueProvider> Bind(FunctionBindingContext context, IDictionary<string, object> parameters)
        {
            Dictionary<string, IValueProvider> results = new Dictionary<string, IValueProvider>();
            IReadOnlyDictionary<string, object> bindingData = null;

            BindingContext bindingContext = new BindingContext(context, bindingData);

            foreach (KeyValuePair<string, IBinding> item in _bindings)
            {
                string name = item.Key;
                IBinding binding = item.Value;
                IValueProvider valueProvider;

                try
                {
                    if (parameters != null && parameters.ContainsKey(name))
                    {
                        valueProvider = binding.Bind(parameters[name], context);
                    }
                    else
                    {
                        valueProvider = binding.Bind(bindingContext);
                    }

                }
                catch (Exception exception)
                {
                    valueProvider = new BindingExceptionValueProvider(name, exception);
                }

                results.Add(name, valueProvider);
            }

            return results;
        }
    }
}
