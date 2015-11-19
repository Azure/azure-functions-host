// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Reflection.Emit;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionDescriptor
    {
        public FunctionDescriptor(string name, IFunctionInvoker invoker, Collection<ParameterDescriptor> parameters)
            : this(name, invoker, parameters, new Collection<CustomAttributeBuilder>())
        {
        }

        public FunctionDescriptor(
            string name, 
            IFunctionInvoker invoker, 
            Collection<ParameterDescriptor> parameters, 
            Collection<CustomAttributeBuilder> attributes)
        {
            Name = name;
            Invoker = invoker;
            Parameters = parameters;
            CustomAttributes = attributes;
        }

        public string Name { get; private set; }

        public Collection<ParameterDescriptor> Parameters { get; private set; }

        public Collection<CustomAttributeBuilder> CustomAttributes { get; private set; }

        public IFunctionInvoker Invoker { get; private set; }
    }
}
