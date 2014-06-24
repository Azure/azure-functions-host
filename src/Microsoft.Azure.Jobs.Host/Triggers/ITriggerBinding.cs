using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerBinding
    {
        IReadOnlyDictionary<string, Type> BindingDataContract { get; }

        ITriggerData Bind(object value, ArgumentBindingContext context);

        ITriggerClient CreateClient(MethodInfo method, IReadOnlyDictionary<string, IBinding> nonTriggerBindings,
            FunctionDescriptor functionDescriptor);

        ParameterDescriptor ToParameterDescriptor();
    }
}
