// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal sealed class DotNetFunctionDescriptorProvider : FunctionDescriptorProvider, IDisposable
    {
        private readonly IMetricsLogger _metricsLogger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver> _compilationServiceFactory;

        public DotNetFunctionDescriptorProvider(ScriptHost host, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders, IMetricsLogger metricsLogger, ILoggerFactory loggerFactory)
           : this(host, config, bindingProviders, new DotNetCompilationServiceFactory(loggerFactory), metricsLogger, loggerFactory)
        {
        }

        public DotNetFunctionDescriptorProvider(ScriptHost host, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
            ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver> compilationServiceFactory, IMetricsLogger metricsLogger, ILoggerFactory loggerFactory)
            : base(host, config, bindingProviders)
        {
            _metricsLogger = metricsLogger;
            _loggerFactory = loggerFactory;
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
            if (!_compilationServiceFactory.SupportedLanguages.Contains(functionMetadata.Language))
            {
                return false;
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new DotNetFunctionInvoker(Host,
                functionMetadata,
                inputBindings,
                outputBindings,
                new FunctionEntryPointResolver(functionMetadata.EntryPoint),
                _compilationServiceFactory,
                _loggerFactory,
                _metricsLogger,
                BindingProviders);
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
                ParameterDescriptor descriptor = null;
                foreach (var parameter in parameters)
                {
                    // Is it the trigger parameter?
                    if (string.Compare(parameter.Name, triggerMetadata.Name, StringComparison.Ordinal) == 0)
                    {
                        ParameterDescriptor triggerParameter = CreateTriggerParameter(triggerMetadata, parameter.ParameterType);
                        descriptors.Add(triggerParameter);
                    }
                    else
                    {
                        Type parameterType = parameter.ParameterType;
                        bool parameterIsByRef = parameterType.IsByRef;
                        if (parameterIsByRef)
                        {
                            parameterType = parameterType.GetElementType();
                        }

                        descriptor = new ParameterDescriptor(parameter.Name, parameter.ParameterType);
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

                        // In the C# programming model, IsOut is set for out parameters
                        // In the F# programming model, neither IsOut nor IsIn are set for byref parameters (which are used as out parameters).
                        //   Justification for this cariation of the programming model is that declaring 'out' parameters is (deliberately)
                        //   awkward in F#, they require opening System.Runtime.InteropServices and adding the [<Out>] attribute, and using
                        //   a byref parameter. In contrast declaring a byref parameter alone (neither labelled In nor Out) is simple enough.
                        if (parameter.IsOut || (functionMetadata.Language == DotNetScriptTypes.FSharp && parameterIsByRef && !parameter.IsIn))
                        {
                            descriptor.Attributes |= ParameterAttributes.Out;
                        }

                        descriptors.Add(descriptor);
                    }
                }

                // Add any additional required System parameters (if they haven't already been defined by the user)
                if (!descriptors.Any(p => p.Type == typeof(ExecutionContext)))
                {
                    // Add ExecutionContext to provide access to InvocationId, etc.
                    descriptors.Add(new ParameterDescriptor(ScriptConstants.SystemExecutionContextParameterName, typeof(ExecutionContext)));
                }

                if (TryCreateReturnValueParameterDescriptor(functionTarget.ReturnType, bindings, out descriptor))
                {
                    // If a return value binding has been specified, set up an output
                    // binding to map it to. By convention, this is set up as the last
                    // parameter.
                    descriptors.Add(descriptor);
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

        internal static bool TryCreateReturnValueParameterDescriptor(Type functionReturnType, IEnumerable<FunctionBinding> bindings, out ParameterDescriptor descriptor)
        {
            descriptor = null;
            if (functionReturnType == typeof(Microsoft.FSharp.Core.Unit) ||
                functionReturnType == typeof(Task<Microsoft.FSharp.Core.Unit>))
            {
                return false;
            }
            if (functionReturnType == typeof(void) || functionReturnType == typeof(Task))
            {
                return false;
            }

            // Task<T>
            if (typeof(Task).IsAssignableFrom(functionReturnType))
            {
                if (!(functionReturnType.IsGenericType && functionReturnType.GetGenericTypeDefinition() == typeof(Task<>)))
                {
                    throw new InvalidOperationException($"{ScriptConstants.SystemReturnParameterBindingName} cannot be bound to return type {functionReturnType.Name}.");
                }
                functionReturnType = functionReturnType.GetGenericArguments()[0];
            }

            var byRefType = functionReturnType.MakeByRefType();
            descriptor = new ParameterDescriptor(ScriptConstants.SystemReturnParameterName, byRefType);
            descriptor.Attributes |= ParameterAttributes.Out;

            var returnBinding = bindings.SingleOrDefault(p => p.Metadata.IsReturn);
            if (returnBinding != null)
            {
                Collection<CustomAttributeBuilder> customAttributes = returnBinding.GetCustomAttributes(byRefType);
                if (customAttributes != null)
                {
                    foreach (var customAttribute in customAttributes)
                    {
                        descriptor.CustomAttributes.Add(customAttribute);
                    }
                }
            }

            return true;
        }
    }
}
