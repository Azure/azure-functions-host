// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class BindingProviderContext
    {
        private readonly INameResolver _nameResolver;
        private readonly ParameterInfo _parameter;
        private readonly IReadOnlyDictionary<string, Type> _bindingDataContract;
        private readonly CancellationToken _cancellationToken;

        public BindingProviderContext(INameResolver nameResolver,
            ParameterInfo parameter,
            IReadOnlyDictionary<string, Type> bindingDataContract,
            CancellationToken cancellationToken)
        {
            _nameResolver = nameResolver;
            _parameter = parameter;
            _bindingDataContract = bindingDataContract;
            _cancellationToken = cancellationToken;
        }

        public INameResolver NameResolver
        {
            get { return _nameResolver; }
        }

        public ParameterInfo Parameter
        {
            get { return _parameter; }
        }

        public IReadOnlyDictionary<string, Type> BindingDataContract
        {
            get { return _bindingDataContract; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }

        public string Resolve(string input)
        {
            if (_nameResolver == null)
            {
                return input;
            }

            return _nameResolver.ResolveWholeString(input);
        }

        public static BindingProviderContext Create(BindingContext bindingContext, ParameterInfo parameter,
            IReadOnlyDictionary<string, Type> bindingDataContract)
        {
            return new BindingProviderContext(bindingContext.NameResolver, parameter, bindingDataContract,
                bindingContext.CancellationToken);
        }
    }
}
