// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class BindingProviderContext
    {
        private readonly ParameterInfo _parameter;
        private readonly IReadOnlyDictionary<string, Type> _bindingDataContract;
        private readonly CancellationToken _cancellationToken;

        public BindingProviderContext(ParameterInfo parameter,
            IReadOnlyDictionary<string, Type> bindingDataContract,
            CancellationToken cancellationToken)
        {
            _parameter = parameter;
            _bindingDataContract = bindingDataContract;
            _cancellationToken = cancellationToken;
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
    }
}
