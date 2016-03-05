// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal sealed class CSharpFunctionDescriptionProvider : FunctionDescriptorProvider, IDisposable
    {
        private readonly FunctionAssemblyLoader _assemblyLoader;

        public CSharpFunctionDescriptionProvider(ScriptHost host, ScriptHostConfiguration config)
            : base(host, config)
        {
            _assemblyLoader = new FunctionAssemblyLoader();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _assemblyLoader.Dispose();
            }
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException("functionMetadata");
            }

            functionDescriptor = null;

            string extension = Path.GetExtension(functionMetadata.Source).ToLower(CultureInfo.InvariantCulture);
            if (string.Compare(extension, ".csx", StringComparison.OrdinalIgnoreCase) != 0)
            {
                return false;
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, bool omitInputParameter, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new CSharpFunctionInvoker(Host, functionMetadata, inputBindings, outputBindings, new FunctionEntryPointResolver(), _assemblyLoader);
        }

        protected override Collection<ParameterDescriptor> GetFunctionParameters(IFunctionInvoker functionInvoker, FunctionMetadata functionMetadata,
          BindingMetadata triggerMetadata, Collection<CustomAttributeBuilder> methodAttributes, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            if (functionInvoker == null)
            {
                throw new ArgumentNullException("functionInvoker");
            }
            if (functionMetadata == null)
            {
                throw new ArgumentNullException("functionMetadata");
            }
            if (triggerMetadata == null)
            {
                throw new ArgumentNullException("triggerMetadata");
            }
            if (methodAttributes == null)
            {
                throw new ArgumentNullException("methodAttributes");
            }

            var csharpInvoker = functionInvoker as CSharpFunctionInvoker;
            if (csharpInvoker == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Expected invoker of type '{0}' but received '{1}'", typeof(CSharpFunctionInvoker).Name, functionInvoker.GetType().Name));
            }

            BindingType triggerType = triggerMetadata.Type;
            string triggerParameterName = triggerMetadata.Name;
            bool triggerNameSpecified = true;
            if (string.IsNullOrEmpty(triggerParameterName))
            {
                // default the name to simply 'input'
                triggerMetadata.Name = "input";
                triggerNameSpecified = false;
            }

            try
            {
                MethodInfo functionTarget = csharpInvoker.GetFunctionTarget();
                ParameterInfo[] parameters = functionTarget.GetParameters();
                Collection<ParameterDescriptor> descriptors = new Collection<ParameterDescriptor>();
                IEnumerable<FunctionBinding> bindings = inputBindings.Union(outputBindings);
                foreach (var parameter in parameters)
                {
                    // Is it the trigger parameter?
                    if (string.Compare(parameter.Name, triggerMetadata.Name, StringComparison.Ordinal) == 0)
                    {
                        descriptors.Add(CreateTriggerParameterDescriptor(parameter, triggerMetadata, triggerType, methodAttributes, triggerNameSpecified));
                    }
                    else
                    {
                        Type parameterType = parameter.ParameterType;
                        if (parameterType.IsByRef)
                        {
                            parameterType = parameterType.GetElementType();
                        }

                        var descriptor = new ParameterDescriptor(parameter.Name, parameter.ParameterType);

                        var binding = bindings.FirstOrDefault(b => string.Compare(b.Name, parameter.Name, StringComparison.Ordinal) == 0);
                        if (binding != null)
                        {
                            CustomAttributeBuilder customAttribute = binding.GetCustomAttribute();
                            if (customAttribute != null)
                            {
                                descriptor.CustomAttributes.Add(customAttribute);
                            }
                        }

                        if (parameter.IsOut)
                        {
                            descriptor.Attributes |= ParameterAttributes.Out;
                        }

                        descriptors.Add(descriptor);
                    }
                }

                return descriptors;
            }
            catch (CompilationErrorException)
            {
                // We were unable to compile the function to get its signature,
                // setup the descriptor with the default parameters
                return base.GetFunctionParameters(functionInvoker, functionMetadata, triggerMetadata, methodAttributes, inputBindings, outputBindings);
            }
        }

        private ParameterDescriptor CreateTriggerParameterDescriptor(ParameterInfo parameter, BindingMetadata triggerMetadata,
            BindingType triggerType, Collection<CustomAttributeBuilder> methodAttributes, bool triggerNameSpecified)
        {
            ParameterDescriptor triggerParameter = null;
            switch (triggerType)
            {
                case BindingType.QueueTrigger:
                    triggerParameter = ParseQueueTrigger((QueueBindingMetadata)triggerMetadata, parameter.ParameterType);
                    break;
                case BindingType.EventHubTrigger:
                    triggerParameter = ParseEventHubTrigger((EventHubBindingMetadata)triggerMetadata, parameter.ParameterType);
                    break;
                case BindingType.BlobTrigger:
                    triggerParameter = ParseBlobTrigger((BlobBindingMetadata)triggerMetadata, parameter.ParameterType);
                    break;
                case BindingType.ServiceBusTrigger:
                    triggerParameter = ParseServiceBusTrigger((ServiceBusBindingMetadata)triggerMetadata, parameter.ParameterType);
                    break;
                case BindingType.TimerTrigger:
                    triggerParameter = ParseTimerTrigger((TimerBindingMetadata)triggerMetadata, parameter.ParameterType);
                    break;
                case BindingType.HttpTrigger:
                    if (!triggerNameSpecified)
                    {
                        triggerMetadata.Name = "req";
                    }
                    triggerParameter = ParseHttpTrigger((HttpTriggerBindingMetadata)triggerMetadata, methodAttributes, parameter.ParameterType);
                    break;
                case BindingType.ManualTrigger:
                    triggerParameter = ParseManualTrigger(triggerMetadata, methodAttributes, parameter.ParameterType);
                    break;
            }

            triggerParameter.IsTrigger = true;

            return triggerParameter;
        }
    }
}
