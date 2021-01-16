// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
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

            TriggerParameter = Parameters?.FirstOrDefault(p => p.IsTrigger);
            TriggerBinding = InputBindings?.SingleOrDefault(p => p.Metadata.IsTrigger);
            HttpTriggerAttribute = GetTriggerAttributeOrNull<HttpTriggerAttribute>();
        }

        public string Name { get; internal set; }

        public Collection<ParameterDescriptor> Parameters { get; }

        public Collection<CustomAttributeBuilder> CustomAttributes { get; }

        public IFunctionInvoker Invoker { get; internal set; }

        public FunctionMetadata Metadata { get; internal set; }

        public Collection<FunctionBinding> InputBindings { get; }

        public Collection<FunctionBinding> OutputBindings { get; }

        public ParameterDescriptor TriggerParameter { get; }

        public FunctionBinding TriggerBinding { get; }

        public virtual HttpTriggerAttribute HttpTriggerAttribute { get; }

        private TAttribute GetTriggerAttributeOrNull<TAttribute>()
        {
            var extensionBinding = TriggerBinding as ExtensionBinding;
            if (extensionBinding != null)
            {
                return extensionBinding.Attributes.OfType<TAttribute>().SingleOrDefault();
            }

            return default(TAttribute);
        }
    }
}
