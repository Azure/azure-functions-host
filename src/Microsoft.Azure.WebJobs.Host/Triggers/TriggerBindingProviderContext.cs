// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    internal class TriggerBindingProviderContext
    {
        private readonly INameResolver _nameResolver;
        private readonly ParameterInfo _parameter;
        private readonly CancellationToken _cancellationToken;

        public TriggerBindingProviderContext(INameResolver nameResolver, ParameterInfo parameter, 
            CancellationToken cancellationToken)
        {
            _nameResolver = nameResolver;
            _parameter = parameter;
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
    }
}
