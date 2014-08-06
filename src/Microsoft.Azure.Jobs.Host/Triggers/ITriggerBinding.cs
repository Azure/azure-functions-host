// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerBinding
    {
        IReadOnlyDictionary<string, Type> BindingDataContract { get; }

        Task<ITriggerData> BindAsync(object value, ValueBindingContext context);

        IFunctionDefinition CreateFunctionDefinition(IReadOnlyDictionary<string, IBinding> nonTriggerBindings,
            FunctionDescriptor functionDescriptor, MethodInfo method);

        ParameterDescriptor ToParameterDescriptor();
    }
}
