// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal abstract class WorkerFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IFunctionInvocationDispatcher _dispatcher;
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly TimeSpan _workerInitializationTimeout;
        private readonly Regex _expressionRegex;

        public WorkerFunctionDescriptorProvider(ScriptHost host, ScriptJobHostOptions config, ICollection<IScriptBindingProvider> bindingProviders,
            IFunctionInvocationDispatcher dispatcher, ILoggerFactory loggerFactory, IApplicationLifetime applicationLifetime, TimeSpan workerInitializationTimeout)
            : base(host, config, bindingProviders)
        {
            _dispatcher = dispatcher;
            _loggerFactory = loggerFactory;
            _applicationLifetime = applicationLifetime;
            _workerInitializationTimeout = workerInitializationTimeout;
            _expressionRegex = new Regex(@"{(.*?)\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        public override async Task<(bool Success, FunctionDescriptor Descriptor)> TryCreate(FunctionMetadata functionMetadata)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }

            // If a function exists exists with a proxy, there is a chance this could get evaluated first before ProxyFunctionDescriptorProvider.
            if (functionMetadata.IsProxy())
            {
                return (false, null);
            }

            return await base.TryCreate(functionMetadata);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new WorkerFunctionInvoker(Host, triggerMetadata, functionMetadata, _loggerFactory, inputBindings, outputBindings, _dispatcher, _applicationLifetime, _workerInitializationTimeout);
        }

        protected override async Task<Collection<ParameterDescriptor>> GetFunctionParametersAsync(IFunctionInvoker functionInvoker, FunctionMetadata functionMetadata,
            BindingMetadata triggerMetadata, Collection<CustomAttributeBuilder> methodAttributes, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            var bindings = inputBindings.Union(outputBindings);

            // If an input or output binding uses expressions, we do not want to use ParameterBindingData for the trigger bindings
            if (triggerMetadata.SupportsDeferredBinding())
            {
                bool skipDeferredBinding = BindingAttributeContainsExpression(bindings);
                if (skipDeferredBinding)
                {
                    triggerMetadata.Properties.Add(ScriptConstants.SkipDeferredBindingKey, true);
                }
            }

            var parameters = await base.GetFunctionParametersAsync(functionInvoker, functionMetadata, triggerMetadata, methodAttributes, inputBindings, outputBindings);

            // Add cancellation token
            parameters.Add(new ParameterDescriptor(ScriptConstants.SystemCancellationTokenParameterName, typeof(CancellationToken)));

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

        internal bool BindingAttributeContainsExpression(IEnumerable<FunctionBinding> bindings)
        {
            foreach (ExtensionBinding binding in bindings)
            {
                if (binding.Metadata.IsTrigger)
                {
                    // skip triggers, we only care if input and output bindings contain expressions
                    continue;
                }

                foreach (var attribute in binding.Attributes)
                {
                    return attribute.GetType()
                                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                    .Any(prop => prop.PropertyType == typeof(string) && IsMatch((string)prop.GetValue(attribute)));
                }
            }

            return false;
        }

        private bool IsMatch(string value)
        {
            return string.IsNullOrEmpty(value) ? false : _expressionRegex.IsMatch(value);
        }
    }
}
