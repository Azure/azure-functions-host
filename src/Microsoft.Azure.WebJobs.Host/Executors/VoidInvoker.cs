// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class VoidInvoker : IInvoker
    {
        private readonly IReadOnlyList<string> _parameterNames;
        private readonly Action<object[]> _lambda;

        public VoidInvoker(IReadOnlyList<string> parameterNames, Action<object[]> lambda)
        {
            _parameterNames = parameterNames;
            _lambda = lambda;
        }

        public IReadOnlyList<string> ParameterNames
        {
            get { return _parameterNames; }
        }

        public Task InvokeAsync(object[] arguments)
        {
            _lambda.Invoke(arguments);
            return Task.FromResult(0);
        }
    }
}
