// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [DebuggerDisplay("{Name} ({Metadata.ScriptType})")]
    public class FunctionDescriptor
    {
        // For unit tests.
        internal FunctionDescriptor()
        {
        }

        public FunctionDescriptor(
            string name,
            IFunctionInvoker invoker,
            FunctionMetadata metadata,
            Collection<ParameterDescriptor> parameters,
            Collection<CustomAttributeBuilder> attributes,
            Collection<FunctionBinding> inputBindings,
            Collection<FunctionBinding> outputBindings)
        {
            Name = name;
            Invoker = invoker;
            Parameters = parameters;
            CustomAttributes = attributes;
            Metadata = metadata;
            InputBindings = inputBindings;
            OutputBindings = outputBindings;
        }

        public string Name { get; internal set; }

        public Collection<ParameterDescriptor> Parameters { get; private set; }

        public Collection<CustomAttributeBuilder> CustomAttributes { get; private set; }

        public IFunctionInvoker Invoker { get; internal set; }

        public FunctionMetadata Metadata { get; internal set; }

        public Collection<FunctionBinding> InputBindings { get; set; }

        public Collection<FunctionBinding> OutputBindings { get; set; }

        public virtual TAttribute GetTriggerAttributeOrNull<TAttribute>()
        {
            var triggerBinding = InputBindings.SingleOrDefault(p => p.Metadata.IsTrigger);
            if (triggerBinding != null)
            {
                ExtensionBinding extensionBinding = triggerBinding as ExtensionBinding;
                if (extensionBinding != null)
                {
                    return extensionBinding.Attributes.OfType<TAttribute>().SingleOrDefault();
                }
            }

            return default(TAttribute);
        }
    }
}
