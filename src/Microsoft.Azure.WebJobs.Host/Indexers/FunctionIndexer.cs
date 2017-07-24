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
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal class FunctionIndexer
    {
        public const string ReturnParamName = "$return";

        private static readonly BindingFlags PublicMethodFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private readonly ITriggerBindingProvider _triggerBindingProvider;
        private readonly IBindingProvider _bindingProvider;
        private readonly IJobActivator _activator;
        private readonly INameResolver _nameResolver;
        private readonly IFunctionExecutor _executor;
        private readonly HashSet<Assembly> _jobAttributeAssemblies;
        private readonly SingletonManager _singletonManager;
        private readonly TraceWriter _trace;
        private readonly ILogger _logger;

        public FunctionIndexer(
            ITriggerBindingProvider triggerBindingProvider, 
            IBindingProvider bindingProvider, 
            IJobActivator activator, 
            IFunctionExecutor executor, 
            IExtensionRegistry extensions, 
            SingletonManager singletonManager,
            TraceWriter trace, 
            ILoggerFactory loggerFactory,
            INameResolver nameResolver = null)
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
            _nameResolver = nameResolver;
            _trace = trace;
            _logger = loggerFactory?.CreateLogger(LogCategories.Startup);
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
                    fex.TryRecover(_trace, _logger);
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

            if (method.ReturnParameter.GetCustomAttributesData().Any(HasJobAttribute))
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
            IEnumerable<ParameterInfo> parameters = method.GetParameters();

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

            ReturnParameterInfo returnParameter = null;
            bool triggerHasReturnBinding = false;

            Type methodReturnType;
            if (TypeUtility.TryGetReturnType(method, out methodReturnType))
            {
                Type triggerReturnType;
                if (bindingDataContract != null && bindingDataContract.TryGetValue(ReturnParamName, out triggerReturnType))
                {
                    // The trigger will handle the return value.
                    triggerHasReturnBinding = true;
                }
                
                // We treat binding to the return type the same as binding to an 'out T' parameter. 
                // An explicit return binding takes precedence over an implicit trigger binding. 
                returnParameter = new ReturnParameterInfo(method, methodReturnType);
                parameters = parameters.Concat(new ParameterInfo[] { returnParameter });                
            }

            foreach (ParameterInfo parameter in parameters)
            {
                if (parameter == triggerParameter)
                {
                    continue;
                }

                IBinding binding = await _bindingProvider.TryCreateAsync(new BindingProviderContext(parameter, bindingDataContract, cancellationToken));
                if (binding == null)
                {
                    if (parameter == returnParameter)
                    {
                        if (triggerHasReturnBinding)
                        {
                            // Ok. Skip and let trigger own the return binding. 
                            continue;
                        }
                        else
                        {
                            // If the method has a return value, then we must have a binding to it. 
                            // This is similar to the error we used to throw. 
                            invalidInvokeBindingException = new InvalidOperationException("Functions must return Task or void, have a binding attribute for the return value, or be triggered by a binding that natively supports return values.");
                        }
                    }

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
                string msg = $"Function '{method.Name}' is async but does not return a Task. Your function may not run correctly.";
                _trace.Warning(msg);
                _logger?.LogWarning(msg);
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
                functionDefinition = (FunctionDefinition)methodInfo.Invoke(this, new object[] { triggerBinding, triggerParameterName, functionDescriptor, nonTriggerBindings, invoker });

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
            IListenerFactory listenerFactory = new ListenerFactory(descriptor, triggerExecutor, triggerBinding);

            return new FunctionDefinition(descriptor, instanceFactory, listenerFactory);
        }

        // Expose internally for testing purposes 
        internal static FunctionDescriptor FromMethod(
            MethodInfo method, 
            IJobActivator jobActivator = null,
            INameResolver nameResolver = null)
        {
            var disabled = HostListenerFactory.IsDisabled(method, nameResolver, jobActivator);

            // Determine the TraceLevel for this function (affecting both Console as well as Dashboard logging)
            TraceLevelAttribute traceAttribute = TypeUtility.GetHierarchicalAttributeOrNull<TraceLevelAttribute>(method);

            bool hasCancellationToken = method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken));

            string logName = method.Name;
            string shortName = method.GetShortName();
            FunctionNameAttribute nameAttribute = method.GetCustomAttribute<FunctionNameAttribute>();
            if (nameAttribute != null)
            {
                logName = nameAttribute.Name;
                shortName = logName;
                if (!FunctionNameAttribute.FunctionNameValidationRegex.IsMatch(logName))
                {
                    throw new InvalidOperationException(string.Format("'{0}' is not a valid function name.", logName));
                }
            }

            return new FunctionDescriptor
            {
                Id = method.GetFullName(),
                LogName = logName,
                FullName = method.GetFullName(),
                ShortName = shortName,
                IsDisabled = disabled,
                HasCancellationToken = hasCancellationToken,
                TraceLevel = traceAttribute?.Level ?? TraceLevel.Verbose,
                TimeoutAttribute = TypeUtility.GetHierarchicalAttributeOrNull<TimeoutAttribute>(method),
                SingletonAttributes = method.GetCustomAttributes<SingletonAttribute>()
            };
        }

        private FunctionDescriptor CreateFunctionDescriptor(MethodInfo method, string triggerParameterName,
            ITriggerBinding triggerBinding, IReadOnlyDictionary<string, IBinding> nonTriggerBindings)
        {
            var descr = FromMethod(method, this._activator, _nameResolver);

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
                        
            descr.Parameters = parameters;
            descr.TriggerParameterDescriptor = parameters.OfType<TriggerParameterDescriptor>().FirstOrDefault();

            return descr;
        }

        private class ListenerFactory : IListenerFactory
        {
            private readonly FunctionDescriptor _descriptor;
            private readonly ITriggeredFunctionExecutor _executor;
            private readonly ITriggerBinding _binding;

            public ListenerFactory(FunctionDescriptor descriptor, ITriggeredFunctionExecutor executor, ITriggerBinding binding)
            {
                _descriptor = descriptor;
                _executor = executor;
                _binding = binding;
            }

            public Task<IListener> CreateAsync(CancellationToken cancellationToken)
            {
                ListenerFactoryContext context = new ListenerFactoryContext(_descriptor, _executor, cancellationToken);
                return _binding.CreateListenerAsync(context);
            }
        }

        // Get a ParameterInfo that represents the return type as a parameter. 
        private class ReturnParameterInfo : ParameterInfo
        {
            private readonly IEnumerable<Attribute> _attributes;

            public ReturnParameterInfo(MethodInfo method, Type methodReturnType)
            {
                // If Method is Task<T>, then unwrap to jsut T. 
                var retType = methodReturnType.MakeByRefType(); // 'return T' is 'out T'
                ClassImpl = retType;
                AttrsImpl = ParameterAttributes.Out;
                NameImpl = ReturnParamName;
                MemberImpl = method;

                // union all the parameter attributes
                _attributes = method.ReturnParameter.GetCustomAttributes();
            }

            public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            {
                return _attributes.Where(p => p.GetType() == attributeType).ToArray();
            }
        }
    }
}
