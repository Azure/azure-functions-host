// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection.Emit;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [DebuggerDisplay("{Name} ({Metadata.ScriptType})")]
    public class FunctionDescriptor
    {
        public FunctionDescriptor(string name, IFunctionInvoker invoker, FunctionMetadata metadata, Collection<ParameterDescriptor> parameters)
            : this(name, invoker, metadata, parameters, new Collection<CustomAttributeBuilder>())
        {
        }

        public FunctionDescriptor(
            string name, 
            IFunctionInvoker invoker,
            FunctionMetadata metadata,
            Collection<ParameterDescriptor> parameters, 
            Collection<CustomAttributeBuilder> attributes)
        {
            Name = name;
            Invoker = invoker;
            Parameters = parameters;
            CustomAttributes = attributes;
            Metadata = metadata;
        }

        public string Name { get; private set; }

        public Collection<ParameterDescriptor> Parameters { get; private set; }

        public Collection<CustomAttributeBuilder> CustomAttributes { get; private set; }

        public IFunctionInvoker Invoker { get; private set; }

        public FunctionMetadata Metadata { get; private set; }
    }
}
