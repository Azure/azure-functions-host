// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal abstract class WorkerFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private IFunctionInvocationDispatcher _dispatcher;
        private IApplicationLifetime _applicationLifetime;

        public WorkerFunctionDescriptorProvider(ScriptHost host, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
            IFunctionInvocationDispatcher dispatcher, ILoggerFactory loggerFactory, IApplicationLifetime applicationLifetime)
            : base(host, config, bindingProviders)
        {
            _dispatcher = dispatcher;
            _loggerFactory = loggerFactory;
            _applicationLifetime = applicationLifetime;
        }

        public override async Task<(bool, FunctionDescriptor)> TryCreate(FunctionMetadata functionMetadata)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }
            return await base.TryCreate(functionMetadata);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new WorkerFunctionInvoker(Host, triggerMetadata, functionMetadata, _loggerFactory, inputBindings, outputBindings, _dispatcher, _applicationLifetime);
        }

        protected override async Task<Collection<ParameterDescriptor>> GetFunctionParametersAsync(IFunctionInvoker functionInvoker, FunctionMetadata functionMetadata,
            BindingMetadata triggerMetadata, Collection<CustomAttributeBuilder> methodAttributes, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            var parameters = await base.GetFunctionParametersAsync(functionInvoker, functionMetadata, triggerMetadata, methodAttributes, inputBindings, outputBindings);

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
