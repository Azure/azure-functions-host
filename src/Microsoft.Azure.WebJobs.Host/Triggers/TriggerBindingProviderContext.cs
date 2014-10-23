// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Triggers
{
    internal class TriggerBindingProviderContext
    {
        private readonly ParameterInfo _parameter;
        private readonly CancellationToken _cancellationToken;

        public TriggerBindingProviderContext(ParameterInfo parameter, CancellationToken cancellationToken)
        {
            _parameter = parameter;
            _cancellationToken = cancellationToken;
        }

        public ParameterInfo Parameter
        {
            get { return _parameter; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }
    }
}
