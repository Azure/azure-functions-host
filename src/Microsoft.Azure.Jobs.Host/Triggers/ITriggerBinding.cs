using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal interface ITriggerBinding
    {
        IReadOnlyDictionary<string, Type> BindingDataContract { get; }

        ITriggerData Bind(object value, ArgumentBindingContext context);

        ParameterDescriptor ToParameterDescriptor();
    }
}
