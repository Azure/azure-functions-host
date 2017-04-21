// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class FunctionDescriptorProvider
    {
        private static readonly Regex BindingNameValidationRegex = new Regex(string.Format("^([a-zA-Z][a-zA-Z0-9]{{0,127}}|{0})$", Regex.Escape(ScriptConstants.SystemReturnParameterBindingName)), RegexOptions.Compiled);

        protected FunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
        {
            Host = host;
            Config = config;
        }

        protected ScriptHost Host { get; private set; }

        protected ScriptHostConfiguration Config { get; private set; }

        public virtual bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new InvalidOperationException("functionMetadata");
            }

            ValidateFunction(functionMetadata);

            // parse the bindings
            Collection<FunctionBinding> inputBindings = FunctionBinding.GetBindings(Config, functionMetadata.InputBindings, FileAccess.Read);
            Collection<FunctionBinding> outputBindings = FunctionBinding.GetBindings(Config, functionMetadata.OutputBindings, FileAccess.Write);

            BindingMetadata triggerMetadata = functionMetadata.InputBindings.FirstOrDefault(p => p.IsTrigger);
            string scriptFilePath = Path.Combine(Config.RootScriptPath, functionMetadata.ScriptFile);
            functionDescriptor = null;
            IFunctionInvoker invoker = null;

            try
            {
                invoker = CreateFunctionInvoker(scriptFilePath, triggerMetadata, functionMetadata, inputBindings, outputBindings);

                Collection<CustomAttributeBuilder> methodAttributes = new Collection<CustomAttributeBuilder>();
                Collection<ParameterDescriptor> parameters = GetFunctionParameters(invoker, functionMetadata, triggerMetadata, methodAttributes, inputBindings, outputBindings);

                functionDescriptor = new FunctionDescriptor(functionMetadata.Name, invoker, functionMetadata, parameters, methodAttributes, inputBindings, outputBindings);

                return true;
            }
            catch (Exception)
            {
                IDisposable disposableInvoker = invoker as IDisposable;
                if (disposableInvoker != null)
                {
                    disposableInvoker.Dispose();
                }

                throw;
            }
        }

        protected virtual Collection<ParameterDescriptor> GetFunctionParameters(IFunctionInvoker functionInvoker, FunctionMetadata functionMetadata,
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

            ApplyMethodLevelAttributes(functionMetadata, triggerMetadata, methodAttributes);

            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            ParameterDescriptor triggerParameter = CreateTriggerParameter(triggerMetadata);
            parameters.Add(triggerParameter);

            // Add a TraceWriter for logging
            parameters.Add(new ParameterDescriptor(ScriptConstants.SystemLogParameterName, typeof(TraceWriter)));

            // Add an IBinder to support the binding programming model
            parameters.Add(new ParameterDescriptor(ScriptConstants.SystemBinderParameterName, typeof(IBinder)));

            // Add ExecutionContext to provide access to InvocationId, etc.
            parameters.Add(new ParameterDescriptor(ScriptConstants.SystemExecutionContextParameterName, typeof(ExecutionContext)));

            parameters.Add(new ParameterDescriptor(ScriptConstants.SystemLoggerParameterName, typeof(ILogger)));

            return parameters;
        }

        protected virtual ParameterDescriptor CreateTriggerParameter(BindingMetadata triggerMetadata, Type parameterType = null)
        {
            ParameterDescriptor triggerParameter = null;
            TryParseTriggerParameter(triggerMetadata.Raw, out triggerParameter, parameterType);
            triggerParameter.IsTrigger = true;

            return triggerParameter;
        }

        private bool TryParseTriggerParameter(JObject metadata, out ParameterDescriptor parameterDescriptor, Type parameterType = null)
        {
            parameterDescriptor = null;

            ScriptBindingContext bindingContext = new ScriptBindingContext(metadata);
            ScriptBinding binding = null;
            foreach (var provider in this.Config.BindingProviders)
            {
                if (provider.TryCreate(bindingContext, out binding))
                {
                    break;
                }
            }

            if (binding != null)
            {
                // Construct the Attribute builders for the binding
                var attributes = binding.GetAttributes();
                Collection<CustomAttributeBuilder> attributeBuilders = new Collection<CustomAttributeBuilder>();
                foreach (var attribute in attributes)
                {
                    var attributeBuilder = ExtensionBinding.GetAttributeBuilder(attribute);
                    attributeBuilders.Add(attributeBuilder);
                }

                Type triggerParameterType = parameterType ?? binding.DefaultType;
                parameterDescriptor = new ParameterDescriptor(bindingContext.Name, triggerParameterType, attributeBuilders);

                return true;
            }

            return false;
        }

        protected internal virtual void ValidateFunction(FunctionMetadata functionMetadata)
        {
            // Functions must have a trigger binding
            var triggerMetadata = functionMetadata.InputBindings.FirstOrDefault(p => p.IsTrigger);
            if (triggerMetadata == null)
            {
                throw new InvalidOperationException("No trigger binding specified. A function must have a trigger input binding.");
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in functionMetadata.Bindings)
            {
                ValidateBinding(binding);

                // Ensure no duplicate binding names
                if (names.Contains(binding.Name))
                {
                    throw new InvalidOperationException(string.Format("Multiple bindings with name '{0}' discovered. Binding names must be unique.", binding.Name));
                }
                else
                {
                    names.Add(binding.Name);
                }
            }
        }

        protected internal virtual void ValidateBinding(BindingMetadata bindingMetadata)
        {
            if (bindingMetadata.Name == null || !BindingNameValidationRegex.IsMatch(bindingMetadata.Name))
            {
                throw new ArgumentException($"The binding name {bindingMetadata.Name} is invalid. Please assign a valid name to the binding.");
            }

            if (bindingMetadata.IsReturn && bindingMetadata.Direction != BindingDirection.Out)
            {
                throw new ArgumentException($"{ScriptConstants.SystemReturnParameterBindingName} bindings must specify a direction of 'out'.");
            }
        }

        protected static void ApplyMethodLevelAttributes(FunctionMetadata functionMetadata, BindingMetadata triggerMetadata, Collection<CustomAttributeBuilder> methodAttributes)
        {
            if (functionMetadata.IsDisabled ||
                (string.Compare("httptrigger", triggerMetadata.Type, StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare("manualtrigger", triggerMetadata.Type, StringComparison.OrdinalIgnoreCase) == 0))
            {
                // the function can be run manually, but there will be no automatic
                // triggering
                ConstructorInfo ctorInfo = typeof(NoAutomaticTriggerAttribute).GetConstructor(new Type[0]);
                CustomAttributeBuilder attributeBuilder = new CustomAttributeBuilder(ctorInfo, new object[0]);
                methodAttributes.Add(attributeBuilder);
            }
        }

        protected abstract IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings);

        protected ParameterDescriptor ParseManualTrigger(BindingMetadata trigger, Type triggerParameterType = null)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException("trigger");
            }

            if (triggerParameterType == null)
            {
                triggerParameterType = typeof(string);
            }

            return new ParameterDescriptor(trigger.Name, triggerParameterType);
        }
    }
}
