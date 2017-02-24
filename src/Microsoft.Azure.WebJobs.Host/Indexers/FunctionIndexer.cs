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
        private readonly HashSet<Assembly> _jobAttributeAssemblies;
        private readonly SingletonManager _singletonManager;
        private readonly TraceWriter _trace;

        public FunctionIndexer(ITriggerBindingProvider triggerBindingProvider, IBindingProvider bindingProvider, IJobActivator activator, IFunctionExecutor executor, IExtensionRegistry extensions, SingletonManager singletonManager, TraceWriter trace)
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

            if (singletonManager == null)
            {
                throw new ArgumentNullException("singletonManager");
            }

            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }

            _triggerBindingProvider = triggerBindingProvider;
            _bindingProvider = bindingProvider;
            _activator = activator;
            _executor = executor;
            _singletonManager = singletonManager;
            _jobAttributeAssemblies = GetJobAttributeAssemblies(extensions);
            _trace = trace;
        }

        public async Task IndexTypeAsync(Type type, IFunctionIndexCollector index, CancellationToken cancellationToken)
        {
            foreach (MethodInfo method in type.GetMethods(PublicMethodFlags).Where(IsJobMethod))
            {
                try
                {
                    await IndexMethodAsync(method, index, cancellationToken);
                }
                catch (FunctionIndexingException fex)
                {
                    fex.TryRecover(_trace);
                    // If recoverable, continue to the rest of the methods.
                    // The method in error simply won't be running in the JobHost.
                    continue;
                }
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

        private static HashSet<Assembly> GetJobAttributeAssemblies(IExtensionRegistry extensions)
        {
            // create a set containing our own core assemblies
            HashSet<Assembly> assemblies = new HashSet<Assembly>();
            assemblies.Add(typeof(BlobAttribute).Assembly);
       
            // add any extension assemblies
            assemblies.UnionWith(extensions.GetExtensionAssemblies());

            return assemblies;
        }

        private bool HasJobAttribute(CustomAttributeData attributeData)
        {
            return _jobAttributeAssemblies.Contains(attributeData.AttributeType.Assembly);
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
                throw new FunctionIndexingException(method.GetShortName(), exception);
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
                        throw new InvalidOperationException(
                            string.Format(Constants.UnableToBindParameterFormat, 
                            parameter.Name, parameter.ParameterType.Name, Constants.ExtensionInitializationMessage));
                    }
                    else
                    {
                        // Host.Call-only parameter
                        binding = InvokeBinding.Create(parameter.Name, parameter.ParameterType);
                        if (binding == null && invalidInvokeBindingException == null)
                        {
                            // This function might not have any attribute, in which case we shouldn't throw an
                            // exception when we can't bind it. Instead, save this exception for later once we determine
                            // whether or not it is an SDK function.
                            invalidInvokeBindingException = new InvalidOperationException(
                                string.Format(Constants.UnableToBindParameterFormat,
                                parameter.Name, parameter.ParameterType.Name, Constants.ExtensionInitializationMessage));
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

            if (TypeUtility.IsAsyncVoid(method))
            {
                this._trace.Warning($"Function '{method.Name}' is async but does not return a Task. Your function may not run correctly.");
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
            if (nonTriggerBindings.OfType<TraceWriterBinding>().Count() > 1)
            {
                throw new InvalidOperationException("Can't have multiple TraceWriter/TextWriter parameters in a single function.");
            }

            string triggerParameterName = triggerParameter != null ? triggerParameter.Name : null;
            FunctionDescriptor functionDescriptor = CreateFunctionDescriptor(method, triggerParameterName, triggerBinding, nonTriggerBindings);
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method, _activator);
            IFunctionDefinition functionDefinition;

            if (triggerBinding != null)
            {
                Type triggerValueType = triggerBinding.TriggerValueType;
                var methodInfo = typeof(FunctionIndexer).GetMethod("CreateTriggeredFunctionDefinition", BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(triggerValueType);
                functionDefinition = (FunctionDefinition)methodInfo.Invoke(null, new object[] { triggerBinding, triggerParameterName, functionDescriptor, nonTriggerBindings, invoker });

                if (hasNoAutomaticTriggerAttribute && functionDefinition != null)
                {
                    functionDefinition = new FunctionDefinition(functionDescriptor, functionDefinition.InstanceFactory, listenerFactory: null);
                }
            }
            else
            {
                IFunctionInstanceFactory instanceFactory = new FunctionInstanceFactory(new FunctionBinding(functionDescriptor, nonTriggerBindings, _singletonManager), invoker, functionDescriptor);
                functionDefinition = new FunctionDefinition(functionDescriptor, instanceFactory, listenerFactory: null);
            }

            index.Add(functionDefinition, functionDescriptor, method);
        }

        private FunctionDefinition CreateTriggeredFunctionDefinition<TTriggerValue>(
            ITriggerBinding triggerBinding, string parameterName, FunctionDescriptor descriptor,
            IReadOnlyDictionary<string, IBinding> nonTriggerBindings, IFunctionInvoker invoker)
        {
            ITriggeredFunctionBinding<TTriggerValue> functionBinding = new TriggeredFunctionBinding<TTriggerValue>(descriptor, parameterName, triggerBinding, nonTriggerBindings, _singletonManager);
            ITriggeredFunctionInstanceFactory<TTriggerValue> instanceFactory = new TriggeredFunctionInstanceFactory<TTriggerValue>(functionBinding, invoker, descriptor);
            ITriggeredFunctionExecutor triggerExecutor = new TriggeredFunctionExecutor<TTriggerValue>(descriptor, _executor, instanceFactory);
            IListenerFactory listenerFactory = new ListenerFactory(descriptor, triggerExecutor, triggerBinding, _trace);

            return new FunctionDefinition(descriptor, instanceFactory, listenerFactory);
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
                Method = method,
                FullName = method.GetFullName(),
                ShortName = method.GetShortName(),
                Parameters = parameters
            };
        }

        internal class ListenerFactory : IListenerFactory
        {
            private readonly FunctionDescriptor _descriptor;
            private readonly ITriggeredFunctionExecutor _executor;
            private readonly ITriggerBinding _binding;
            private readonly TraceWriter _trace;

            public ListenerFactory(FunctionDescriptor descriptor, ITriggeredFunctionExecutor executor, ITriggerBinding binding, TraceWriter trace)
            {
                _descriptor = descriptor;
                _executor = executor;
                _binding = binding;
                _trace = trace;
            }

            public async Task<IListener> CreateAsync(CancellationToken cancellationToken)
            {
                ListenerFactoryContext context = new ListenerFactoryContext(_descriptor, _executor, cancellationToken);
                IListener listener = await _binding.CreateListenerAsync(context);
                return new TriggerListener(listener, _descriptor, _trace);
            }

            private class TriggerListener : IListener
            {
                private readonly IListener _listener;
                private readonly FunctionDescriptor _descriptor;
                private readonly TraceWriter _trace;

                public TriggerListener(IListener listener, FunctionDescriptor descriptor, TraceWriter trace)
                {
                    _listener = listener;
                    _descriptor = descriptor;
                    _trace = trace;
                }

                public void Cancel()
                {
                    _listener.Cancel();
                }

                public void Dispose()
                {
                    _listener.Dispose();
                }

                public async Task StartAsync(CancellationToken cancellationToken)
                {
                    try
                    {
                        await _listener.StartAsync(cancellationToken);
                    }
                    catch (Exception e)
                    {
                        new FunctionListenerException(_descriptor.ShortName, e).TryRecover(_trace);
                    }
                }

                public Task StopAsync(CancellationToken cancellationToken)
                {
                    return _listener.StopAsync(cancellationToken);
                }
            }
        }
    }
}
