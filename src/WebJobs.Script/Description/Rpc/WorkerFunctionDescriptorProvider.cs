// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class WorkerFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private IFunctionRegistry _dispatcher;

        public WorkerFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config, IFunctionRegistry dispatcher)
            : base(host, config)
        {
            _dispatcher = dispatcher;
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }
            functionDescriptor = null;
            return _dispatcher.IsSupported(functionMetadata)
                && base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            var inputBuffer = new BufferBlock<ScriptInvocationContext>();
            _dispatcher.Register(new FunctionRegistrationContext
            {
                Metadata = functionMetadata,
                InputBuffer = inputBuffer
            });
            return new WorkerLanguageInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings, inputBuffer);
        }

        protected override Collection<ParameterDescriptor> GetFunctionParameters(IFunctionInvoker functionInvoker, FunctionMetadata functionMetadata,
            BindingMetadata triggerMetadata, Collection<CustomAttributeBuilder> methodAttributes, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            var parameters = base.GetFunctionParameters(functionInvoker, functionMetadata, triggerMetadata, methodAttributes, inputBindings, outputBindings);

            var bindings = inputBindings.Union(outputBindings);

            try
            {
                var triggerHandlesReturnValueBinding = bindings.SingleOrDefault(b =>
                    b.Metadata.IsTrigger &&
                    (b as ExtensionBinding)?.Attributes.SingleOrDefault(a =>
                        (a.GetType().GetCustomAttribute(typeof(BindingAttribute)) as BindingAttribute)?.TriggerHandlesReturnValue == true)
                    != null);

                if (triggerHandlesReturnValueBinding != null)
                {
                    var byRefType = typeof(object).MakeByRefType();

                    ParameterDescriptor returnDescriptor = new ParameterDescriptor(ScriptConstants.SystemReturnParameterName, byRefType);
                    returnDescriptor.Attributes |= ParameterAttributes.Out;

                    Collection<CustomAttributeBuilder> customAttributes = triggerHandlesReturnValueBinding.GetCustomAttributes(byRefType);
                    if (customAttributes != null)
                    {
                        foreach (var customAttribute in customAttributes)
                        {
                            returnDescriptor.CustomAttributes.Add(customAttribute);
                        }
                    }

                    parameters.Add(returnDescriptor);
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("Multiple bindings cannot be designated as HandlesReturnValue.", ex);
            }

            return parameters;
        }
    }
}
