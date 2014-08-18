// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class FunctionInstanceFactory : IFunctionInstanceFactory
    {
        private readonly IFunctionBinding _binding;
        private readonly FunctionDescriptor _descriptor;
        private readonly MethodInfo _method;

        public FunctionInstanceFactory(IFunctionBinding binding, FunctionDescriptor descriptor, MethodInfo method)
        {
            _binding = binding;
            _descriptor = descriptor;
            _method = method;
        }

        public IFunctionInstance Create(Guid id, Guid? parentId, ExecutionReason reason,
            IDictionary<string, object> parameters)
        {
            IBindingSource bindingSource = new BindingSource(_binding, parameters);
            return new FunctionInstance(id, parentId, reason, bindingSource, _descriptor, _method);
        }
    }
}
