// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.ConsoleOutput;
using Microsoft.Azure.WebJobs.Host.Bindings.Invoke;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal class FunctionIndexer
    {
        private static readonly BindingFlags PublicMethodFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private readonly ITriggerBindingProvider _triggerBindingProvider;
        private readonly IBindingProvider _bindingProvider;
        private readonly IJobActivator _activator;
        private readonly IFunctionExecutor _executor;
        private readonly HashSet<Assembly> _jobTypeAssemblies;

        public FunctionIndexer(ITriggerBindingProvider triggerBindingProvider, IBindingProvider bindingProvider, IJobActivator activator, IFunctionExecutor executor, IExtensionRegistry extensions)
        {
            if (triggerBindingProvider == null)
            {
                throw new ArgumentNullException("triggerBindingProvider");
            }

            if (bindingProvider == null)
            {
                throw new ArgumentNullException("bindingProvider");
            }

            if (activator == null)
            {
                throw new ArgumentNullException("activator");
            }

            if (executor == null)
            {
                throw new ArgumentNullException("executor");
            }

            if (extensions == null)
            {
                throw new ArgumentNullException("extensions");
            }

            _triggerBindingProvider = triggerBindingProvider;
            _bindingProvider = bindingProvider;
            _activator = activator;
            _executor = executor;
            _jobTypeAssemblies = new HashSet<Assembly>(GetJobTypeAssemblies(extensions, typeof(ITriggerBindingProvider), typeof(IBindingProvider)));
        }

        public async Task IndexTypeAsync(Type type, IFunctionIndexCollector index, CancellationToken cancellationToken)
        {
            foreach (MethodInfo method in type.GetMethods(PublicMethodFlags).Where(IsJobMethod))
            {
                await IndexMethodAsync(method, index, cancellationToken);
            }
        }

        public bool IsJobMethod(MethodInfo method)
        {
            if (method.ContainsGenericParameters)
            {
                return false;
            }

            if (method.GetCustomAttributesData().Any(HasJobAttribute))
            {
                return true;
            }

            if (method.GetParameters().Length == 0)
            {
                return false;
            }

            if (method.GetParameters().Any(p => p.GetCustomAttributesData().Any(HasJobAttribute)))
            {
                return true;
            }

            return false;
        }

        private static HashSet<Assembly> GetJobTypeAssemblies(IExtensionRegistry extensions, params Type[] extensionTypes)
        {
            // create a set containing our own core assemblies
            HashSet<Assembly> assemblies = new HashSet<Assembly>();
            assemblies.Add(typeof(BlobAttribute).Assembly);
       
            // add any extension assemblies
            foreach (Type extensionType in extensionTypes)
            {
                var currAssemblies = extensions.GetExtensions(extensionType).Select(p => p.GetType().Assembly);
                assemblies.UnionWith(currAssemblies);
            }

            return assemblies;
        }

        private bool HasJobAttribute(CustomAttributeData attributeData)
        {
            return _jobTypeAssemblies.Contains(attributeData.AttributeType.Assembly);
        }

        public async Task IndexMethodAsync(MethodInfo method, IFunctionIndexCollector index, CancellationToken cancellationToken)
        {
            try
            {
                await IndexMethodAsyncCore(method, index, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new FunctionIndexingException(method.Name, exception);
            }
        }

        internal async Task IndexMethodAsyncCore(MethodInfo method, IFunctionIndexCollector index, CancellationToken cancellationToken)
        {
            Debug.Assert(method != null);
            bool hasNoAutomaticTriggerAttribute = method.GetCustomAttribute<NoAutomaticTriggerAttribute>() != null;

            ITriggerBinding triggerBinding = null;
            ParameterInfo triggerParameter = null;
            ParameterInfo[] parameters = method.GetParameters();

            foreach (ParameterInfo parameter in parameters)
            {
                ITriggerBinding possibleTriggerBinding = await _triggerBindingProvider.TryCreateAsync(new TriggerBindingProviderContext(parameter, cancellationToken));

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

                IBinding binding = await _bindingProvider.TryCreateAsync(new BindingProviderContext(parameter, bindingDataContract, cancellationToken));
                if (binding == null)
                {
                    if (triggerBinding != null && !hasNoAutomaticTriggerAttribute)
                    {
                        throw new InvalidOperationException(string.Format("Cannot bind parameter '{0}' when using this trigger.", parameter.Name));
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
                            // whether or not it is an SDK function.
                            invalidInvokeBindingException = new InvalidOperationException(string.Format("Cannot bind parameter '{0}' to type {1}.", parameterName, parameterType.Name));
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

            Type returnType = method.ReturnType;

            if (returnType != typeof(void) && returnType != typeof(Task))
            {
                throw new InvalidOperationException("Functions must return Task or void.");
            }

            if (invalidInvokeBindingException != null)
            {
                throw invalidInvokeBindingException;
            }

            // Validation: prevent multiple ConsoleOutputs
            if (nonTriggerBindings.OfType<ConsoleOutputBinding>().Count() > 1)
            {
                throw new InvalidOperationException("Can't have multiple console output TextWriter parameters on a single function.");
            }

            string triggerParameterName = triggerParameter != null ? triggerParameter.Name : null;
            FunctionDescriptor functionDescriptor = CreateFunctionDescriptor(method, triggerParameterName, triggerBinding, nonTriggerBindings);
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method, _activator);
            IFunctionDefinition functionDefinition;

            if (triggerBinding != null)
            {
                functionDefinition = CreateTriggeredFunctionDefinition(triggerBinding, triggerParameterName, _executor, functionDescriptor, nonTriggerBindings, invoker);

                if (hasNoAutomaticTriggerAttribute && functionDefinition != null)
                {
                    functionDefinition = new FunctionDefinition(functionDefinition.InstanceFactory, listenerFactory: null);
                }
            }
            else
            {
                IFunctionInstanceFactory instanceFactory = new FunctionInstanceFactory(new FunctionBinding(nonTriggerBindings), invoker, functionDescriptor);
                functionDefinition = new FunctionDefinition(instanceFactory, listenerFactory: null);
            }

            index.Add(functionDefinition, functionDescriptor, method);
        }

        private static FunctionDefinition CreateTriggeredFunctionDefinition(ITriggerBinding triggerBinding, string parameterName, IFunctionExecutor executor, 
            FunctionDescriptor descriptor, IReadOnlyDictionary<string, IBinding> nonTriggerBindings, IFunctionInvoker invoker)
        {
            Type triggerValueType = triggerBinding.TriggerValueType;
            MethodInfo createTriggeredFunctionDefinitionMethodInfo = typeof(FunctionIndexer).GetMethod("CreateTriggeredFunctionDefinitionImpl", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(triggerValueType);
            FunctionDefinition functionDefinition = (FunctionDefinition)createTriggeredFunctionDefinitionMethodInfo.Invoke(null, new object[] { triggerBinding, parameterName, executor, descriptor, nonTriggerBindings, invoker });

            return functionDefinition;
        }

        private static FunctionDefinition CreateTriggeredFunctionDefinitionImpl<TTriggerValue>(
            ITriggerBinding<TTriggerValue> triggerBinding, string parameterName, IFunctionExecutor executor, FunctionDescriptor descriptor,
            IReadOnlyDictionary<string, IBinding> nonTriggerBindings, IFunctionInvoker invoker)
        {
            ITriggeredFunctionBinding<TTriggerValue> functionBinding = new TriggeredFunctionBinding<TTriggerValue>(parameterName, triggerBinding, nonTriggerBindings);
            ITriggeredFunctionInstanceFactory<TTriggerValue> instanceFactory = new TriggeredFunctionInstanceFactory<TTriggerValue>(functionBinding, invoker, descriptor);
            ITriggeredFunctionExecutor<TTriggerValue> triggerExecutor = new TriggeredFunctionExecutor<TTriggerValue>(descriptor, executor, instanceFactory);
            IListenerFactory listenerFactory = triggerBinding.CreateListenerFactory(descriptor, triggerExecutor);

            return new FunctionDefinition(instanceFactory, listenerFactory);
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
