// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.CodeAnalysis.Scripting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal sealed class DotNetFunctionDescriptorProvider : FunctionDescriptorProvider, IDisposable
    {
        private readonly FunctionAssemblyLoader _assemblyLoader;
        private readonly ICompilationServiceFactory _compilationServiceFactory;

        public DotNetFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
           : this(host, config, new DotNetCompilationServiceFactory())
        {
        }

        public DotNetFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config, 
            ICompilationServiceFactory compilationServiceFactory)
            : base(host, config)
        {
            _assemblyLoader = new FunctionAssemblyLoader(config.RootScriptPath);
            _compilationServiceFactory = compilationServiceFactory;
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

            // We can only handle script types supported by the current compilation service factory
            if (!_compilationServiceFactory.SupportedScriptTypes.Contains(functionMetadata.ScriptType))
            {
                return false;
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new DotNetFunctionInvoker(Host, functionMetadata, inputBindings, outputBindings, new FunctionEntryPointResolver(), _assemblyLoader, _compilationServiceFactory);
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

            var dotNetInvoker = functionInvoker as DotNetFunctionInvoker;
            if (dotNetInvoker == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Expected invoker of type '{0}' but received '{1}'", typeof(DotNetFunctionInvoker).Name, functionInvoker.GetType().Name));
            }

            try
            {
                ApplyMethodLevelAttributes(functionMetadata, triggerMetadata, methodAttributes);

                MethodInfo functionTarget = dotNetInvoker.GetFunctionTargetAsync().Result;
                ParameterInfo[] parameters = functionTarget.GetParameters();
                Collection<ParameterDescriptor> descriptors = new Collection<ParameterDescriptor>();
                IEnumerable<FunctionBinding> bindings = inputBindings.Union(outputBindings);
                bool addHttpRequestSystemParameter = false;
                foreach (var parameter in parameters)
                {
                    // Is it the trigger parameter?
                    if (string.Compare(parameter.Name, triggerMetadata.Name, StringComparison.Ordinal) == 0)
                    {
                        ParameterDescriptor triggerParameter = CreateTriggerParameter(triggerMetadata, parameter.ParameterType);
                        descriptors.Add(triggerParameter);

                        if (string.Compare(triggerMetadata.Type, "httptrigger", StringComparison.OrdinalIgnoreCase) == 0 && 
                            parameter.ParameterType != typeof(HttpRequestMessage))
                        {
                            addHttpRequestSystemParameter = true;
                        }
                    }
                    else
                    {
                        Type parameterType = parameter.ParameterType;
                        if (parameterType.IsByRef)
                        {
                            parameterType = parameterType.GetElementType();
                        }

                        var descriptor = new ParameterDescriptor(parameter.Name, parameter.ParameterType);
                        var binding = bindings.FirstOrDefault(b => string.Compare(b.Metadata.Name, parameter.Name, StringComparison.Ordinal) == 0);
                        if (binding != null)
                        {
                            Collection<CustomAttributeBuilder> customAttributes = binding.GetCustomAttributes(parameter.ParameterType);
                            if (customAttributes != null)
                            {
                                foreach (var customAttribute in customAttributes)
                                {
                                    descriptor.CustomAttributes.Add(customAttribute);
                                }
                            }
                        }

                        if (parameter.IsOut)
                        {
                            descriptor.Attributes |= ParameterAttributes.Out;
                        }

                        descriptors.Add(descriptor);
                    }
                }

                // Add any additional common System parameters
                // Add ExecutionContext to provide access to InvocationId, etc.
                descriptors.Add(new ParameterDescriptor("context", typeof(ExecutionContext)));
                
                // If we have an HTTP trigger binding but we're not binding
                // to the HttpRequestMessage, require it as a system parameter
                if (addHttpRequestSystemParameter)
                {
                    descriptors.Add(new ParameterDescriptor(ScriptConstants.DefaultSystemTriggerParameterName, typeof(HttpRequestMessage)));
                }

                return descriptors;
            }
            catch (AggregateException exc)
            {
                if (!(exc.InnerException is CompilationErrorException))
                {
                    throw;
                }
            }
            catch (CompilationErrorException)
            {
            }

            // We were unable to compile the function to get its signature,
            // setup the descriptor with the default parameters
            methodAttributes.Clear();
            return base.GetFunctionParameters(functionInvoker, functionMetadata, triggerMetadata, methodAttributes, inputBindings, outputBindings);
        }
    }
}
