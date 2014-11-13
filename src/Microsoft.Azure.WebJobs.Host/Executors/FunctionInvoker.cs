// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class FunctionInvoker<TReflected> : IFunctionInvoker
    {
        private readonly IReadOnlyList<string> _parameterNames;
        private readonly IMethodInvoker<TReflected> _methodInvoker;
        private readonly IFactory<TReflected> _instanceFactory;

        public FunctionInvoker(IReadOnlyList<string> parameterNames, IMethodInvoker<TReflected> methodInvoker,
            IFactory<TReflected> instanceFactory)
        {
            if (parameterNames == null)
            {
                throw new ArgumentNullException("parameterNames");
            }

            if (methodInvoker == null)
            {
                throw new ArgumentNullException("methodInvoker");
            }

            if (instanceFactory == null)
            {
                throw new ArgumentNullException("instanceFactory");
            }

            _parameterNames = parameterNames;
            _methodInvoker = methodInvoker;
            _instanceFactory = instanceFactory;
        }

        public IFactory<TReflected> InstanceFactory
        {
            get { return _instanceFactory; }
        }

        public IReadOnlyList<string> ParameterNames
        {
            get { return _parameterNames; }
        }

        public Task InvokeAsync(object[] arguments)
        {
            TReflected instance = _instanceFactory.Create();
            return _methodInvoker.InvokeAsync(instance, arguments);
        }
    }
}
