// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Bindings.ConsoleOutput;
using Microsoft.Azure.Jobs.Host.Bindings.Invoke;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    // Go down and build an index
    internal class FunctionIndexer
    {
        private static readonly BindingFlags _publicStaticMethodFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private readonly FunctionIndexerContext _context;
        private readonly ITriggerBindingProvider _triggerBindingProvider;
        private readonly IBindingProvider _bindingProvider;

        public FunctionIndexer(FunctionIndexerContext context)
        {
            _context = context;
            _triggerBindingProvider = context.TriggerBindingProvider;
            _bindingProvider = context.BindingProvider;
        }

        public void IndexType(Type type, IFunctionIndex index)
        {
            foreach (MethodInfo method in type.GetMethods(_publicStaticMethodFlags))
            {
                IndexMethod(method, index);
            }
        }

        public void IndexMethod(MethodInfo method, IFunctionIndex index)
        {
            try
            {
                IndexMethodCore(method, index);
            }
            catch (Exception exception)
            {
                throw new FunctionIndexingException(method.Name, exception);
            }
        }

        private void IndexMethodCore(MethodInfo method, IFunctionIndex index)
        {
            bool hasNoAutomaticTrigger = method.GetCustomAttribute<NoAutomaticTriggerAttribute>() != null;

            ITriggerBinding triggerBinding = null;
            ParameterInfo triggerParameter = null;
            ParameterInfo[] parameters = method.GetParameters();
            foreach (ParameterInfo parameter in parameters)
            {
                ITriggerBinding possibleTriggerBinding = _triggerBindingProvider.TryCreate(
                    new TriggerBindingProviderContext(_context, parameter));

                if (possibleTriggerBinding != null)
                {
                    if (triggerBinding == null)
                    {
                        triggerBinding = possibleTriggerBinding;
                        triggerParameter = parameter;
                    }
                    else
                    {
                        throw new InvalidOperationException("More than one trigger per function is not allowed.");
                    }
                }
            }

            Dictionary<string, IBinding> nonTriggerBindings = new Dictionary<string, IBinding>();
            IReadOnlyDictionary<string, Type> bindingDataContract;

            if (triggerBinding != null)
            {
                bindingDataContract = triggerBinding.BindingDataContract;
            }
            else
            {
                bindingDataContract = null;
            }

            bool hasParameterBindingAttribute = false;
            Exception invalidInvokeBindingException = null;

            foreach (ParameterInfo parameter in parameters)
            {
                if (parameter == triggerParameter)
                {
                    continue;
                }

                IBinding binding = _bindingProvider.TryCreate(
                    BindingProviderContext.Create(_context, parameter, bindingDataContract));

                if (binding == null)
                {
                    if (triggerBinding != null && !hasNoAutomaticTrigger)
                    {
                        throw new InvalidOperationException("Cannot bind parameter '" + parameter.Name + "' when using this trigger.");
                    }
                    else
                    {
                        // Host.Call-only parameter
                        string parameterName = parameter.Name;
                        Type parameterType = parameter.ParameterType;

                        binding = InvokeBinding.Create(parameterName, parameterType);

                        if (binding == null && invalidInvokeBindingException == null)
                        {
                            // This function might not have any attribute, in which case we shouldn't throw an
                            // exception when we can't bind it. Instead, save this exception for later once we determine
                            // whether or not it is an Azure Jobs function.
                            invalidInvokeBindingException = new InvalidOperationException("Cannot bind parameter '" +
                                parameterName + "' to type " + parameterType.Name + ".");
                        }
                    }
                }
                else if (!hasParameterBindingAttribute)
                {
                    hasParameterBindingAttribute = binding.FromAttribute;
                }

                nonTriggerBindings.Add(parameter.Name, binding);
            }

            // Only index functions with some kind of attribute on them. Three ways that could happen:
            // 1. There's an attribute on a trigger parameter (all triggers come from attributes).
            // 2. There's an attribute on a non-trigger parameter (some non-trigger bindings come from attributes).
            if (triggerBinding == null && !hasParameterBindingAttribute)
            {
                // 3. There's an attribute on the method itself (NoAutomaticTrigger).
                if (method.GetCustomAttribute<NoAutomaticTriggerAttribute>() == null)
                {
                    return;
                }
            }

            if (invalidInvokeBindingException != null)
            {
                throw invalidInvokeBindingException;
            }

            // Validation: prevent multiple ConsoleOutputs
            if (nonTriggerBindings.OfType<ConsoleOutputBinding>().Count() > 1)
            {
                throw new InvalidOperationException(
                    "Can't have multiple console output TextWriter parameters on a single function.");
            }

            string triggerParameterName = triggerParameter != null ? triggerParameter.Name : null;
            FunctionDescriptor functionDescriptor = CreateFunctionDescriptor(method, triggerParameterName,
                triggerBinding, nonTriggerBindings);
            IFunctionDefinition functionDefinition;

            if (triggerBinding != null)
            {
                functionDefinition = triggerBinding.CreateFunctionDefinition(nonTriggerBindings,
                    functionDescriptor, method);
            }
            else
            {
                IFunctionInstanceFactory instanceFactory = new FunctionInstanceFactory(
                    new FunctionBinding(method, nonTriggerBindings), functionDescriptor, method);
                functionDefinition = new FunctionDefinition(instanceFactory, listenerFactory: null);
            }

            index.Add(functionDefinition, functionDescriptor, method);
        }

        private static FunctionDescriptor CreateFunctionDescriptor(MethodInfo method, string triggerParameterName,
            ITriggerBinding triggerBinding, IReadOnlyDictionary<string, IBinding> nonTriggerBindings)
        {
            List<ParameterDescriptor> parameters = new List<ParameterDescriptor>();

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                string name = parameter.Name;

                if (name == triggerParameterName)
                {
                    parameters.Add(triggerBinding.ToParameterDescriptor());
                }
                else
                {
                    parameters.Add(nonTriggerBindings[name].ToParameterDescriptor());
                }
            }

            return new FunctionDescriptor
            {
                Id = method.GetFullName(),
                FullName = method.GetFullName(),
                ShortName = method.GetShortName(),
                Parameters = parameters
            };
        }
    }
}
