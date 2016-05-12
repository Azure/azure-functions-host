// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    /// <summary>
    /// Helper class for producing binding rules. 
    /// </summary>
    public class BindingFactory
    {
        private readonly INameResolver _nameResolver;
        private readonly IConverterManager _converterManager;

        /// <summary>
        /// Constructor. 
        /// </summary>
        /// <param name="nameResolver">Name Resolver object for resolving %% tokens in a string.</param>
        /// <param name="converterManager">Converter Manager object for resolving {} tokens in a string. </param>
        public BindingFactory(INameResolver nameResolver, IConverterManager converterManager)
        {
            _nameResolver = nameResolver;
            _converterManager = converterManager;
        }

        /// <summary>
        /// Get the name resolver for resolving %% tokens. 
        /// </summary>
        public INameResolver NameResolver
        {
            get { return _nameResolver; }
        }

        /// <summary>
        /// Get the converter manager for coercing types. 
        /// </summary>
        public IConverterManager ConverterManager
        {
            get { return _converterManager; }
        }

        /// <summary>
        /// Creating a type filter predicate around another rule. 
        /// </summary>
        /// <param name="predicate">type predicate. Only apply inner rule if this predicate as applied to the user parameter type is true. </param>
        /// <param name="innerRule">Inner rule. </param>
        /// <returns></returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public IBindingProvider AddTypeFilter(Func<Type, bool> predicate, IBindingProvider innerRule)
        {
            return new FilteringBindingProvider(predicate, innerRule);
        }
        
        /// <summary>
        /// Create a rule that returns an IValueBinder from a resolved attribute. IValueBinder will let you have an OnCompleted hook that 
        /// is invoked after the user function completes. 
        /// </summary>
        /// <typeparam name="TAttribute"></typeparam>
        /// <param name="builder"></param>
        /// <returns></returns>
        public IBindingProvider BindToGenericItem<TAttribute>(Func<TAttribute, Type, Task<IValueBinder>> builder)
            where TAttribute : Attribute
        {
            return new ItemBindingProvider<TAttribute>(this._nameResolver, builder);
        }

        /// <summary>
        /// Create a rule for binding a parameter to an <see cref="IAsyncCollector{TMEssage}"/>. 
        /// Use the <see cref="IConverterManager"/> to convert form the user's parameter type to the TMessage type. 
        /// </summary>
        /// <typeparam name="TAttribute">type of binding attribute</typeparam>
        /// <typeparam name="TMessage">'core type' for the IAsyncCollector.</typeparam>
        /// <param name="buildFromAttribute">function to allocate the collector object given a resolved instance of the attribute.</param>
        /// <param name="postResolveHook"></param>
        /// <param name="buildParameterDescriptor"></param>
        /// <returns></returns>
        public IBindingProvider BindToAsyncCollector<TAttribute, TMessage>(
            Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute, 
            Func<TAttribute, ParameterInfo, INameResolver, ParameterDescriptor> buildParameterDescriptor = null,
            Func<TAttribute, ParameterInfo, INameResolver, Task<TAttribute>> postResolveHook = null)
            where TAttribute : Attribute
        {
            return new AsyncCollectorWithConverterManagerBindingProvider<TAttribute, TMessage>(
                _nameResolver, _converterManager, buildFromAttribute, buildParameterDescriptor, postResolveHook);
        }

        /// <summary>
        /// Create a rule for binding a parameter to an <see cref="IAsyncCollector{T}"/> where T is the user parameter's type. 
        /// </summary>
        /// <typeparam name="TAttribute">type of binding attribute</typeparam>
        /// <typeparam name="TConstructorArgument"></typeparam>
        /// <param name="asyncCollectorType">type that implements <see cref="IAsyncCollector{T}"/>. Must have a constructor with exactly 1 parameter of type TConstructorArgument.</param>
        /// <param name="constructorParameterBuilder">builder function to create an instance of the collector's constructor parameter from a resolved attribute.</param>
        /// <returns></returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
        public IBindingProvider BindToGenericAsyncCollector<TAttribute, TConstructorArgument>(
            Type asyncCollectorType, 
            Func<TAttribute, TConstructorArgument> constructorParameterBuilder)
            where TAttribute : Attribute
        {     
            if (asyncCollectorType == null)
            {
                throw new ArgumentNullException("asyncCollectorType");
            }
            if (constructorParameterBuilder == null)
            {
                throw new ArgumentNullException("constructorParameterBuilder");
            }

            // Verify collector has appropriate constructor. 
                if (!asyncCollectorType.IsGenericTypeDefinition)
            {
                string msg = string.Format(CultureInfo.InvariantCulture, 
                    "Collector implementation type {0} should be a generic where the type will be resolved at runtime. " +
                    "If you know the type at compile time, use a more specific binding rule.",
                  asyncCollectorType.FullName);
                throw new InvalidOperationException(msg);
            }
            var genArgs = asyncCollectorType.GetGenericArguments();
            if (genArgs == null || genArgs.Length != 1)
            {
                string msg = string.Format(CultureInfo.InvariantCulture, "Collector implementation type {0} must have exactly 1 type argument.",
                    asyncCollectorType.FullName);
                throw new InvalidOperationException(msg);
            }
            var ctorInfo = asyncCollectorType.GetConstructor(new Type[] { typeof(TConstructorArgument) });
            if (ctorInfo == null)
            {
                string msg = string.Format(CultureInfo.InvariantCulture, "Collector implementation type {0} must have a public constructor with exactly 2 parameters: {1}, {2}",
                    asyncCollectorType.FullName,
                    typeof(TAttribute).Name, typeof(TConstructorArgument).Name);
                throw new InvalidOperationException(msg);
            }

            return new GenericAsyncCollectorBindingProvider<TAttribute, TConstructorArgument>(
                _nameResolver,  asyncCollectorType, constructorParameterBuilder);
        }

        /// <summary>
        /// Create a binding rule that binds the parameter to an specific instance of TUserType. 
        /// </summary>
        /// <typeparam name="TAttribute">type of binding attribute</typeparam>
        /// <typeparam name="TUserType"></typeparam>
        /// <param name="buildFromAttribute">builder function to create the object that will get passed to the user function.</param>
        /// <returns></returns>
        public IBindingProvider BindToExactType<TAttribute, TUserType>(Func<TAttribute, TUserType> buildFromAttribute)
            where TAttribute : Attribute
        {
            return this.BindToExactAsyncType<TAttribute, TUserType>((attr) => Task.FromResult(buildFromAttribute(attr)));
        }

        /// <summary>
        /// Create a binding rule that binds the parameter to an specific instance of TUserType. 
        /// </summary>
        /// <typeparam name="TAttribute">type of binding attribute</typeparam>
        /// <typeparam name="TUserType"></typeparam>
        /// <param name="buildFromAttribute">builder function to create the object that will get passed to the user function.</param>
        /// <param name="buildParameterDescriptor"></param>
        /// <param name="postResolveHook"></param>
        /// <returns></returns>
        public IBindingProvider BindToExactAsyncType<TAttribute, TUserType>(
            Func<TAttribute, Task<TUserType>> buildFromAttribute,
            Func<TAttribute, ParameterInfo, INameResolver, ParameterDescriptor> buildParameterDescriptor = null,
            Func<TAttribute, ParameterInfo, INameResolver, Task<TAttribute>> postResolveHook = null)
            where TAttribute : Attribute
        {
            var rule = new ExactTypeBindingProvider<TAttribute, TUserType>(_nameResolver, buildFromAttribute, buildParameterDescriptor, postResolveHook);
            return rule;
        }

        /// <summary>
        /// Bind a  parameter to an IAsyncCollector. Use this for things that have discrete output items (like sending messages or writing table rows)
        /// This will add additional adapters to connect the user's parameter type to an IAsyncCollector. 
        /// </summary>
        /// <typeparam name="TMessage"></typeparam>
        /// <typeparam name="TTriggerValue"></typeparam>
        /// <param name="bindingStrategy">a strategy object that describes how to do the binding</param>
        /// <param name="parameter">the user's parameter being bound to</param>
        /// <param name="converterManager">the converter manager, used to convert between the user parameter's type and the underlying native types used by the trigger strategy</param>
        /// <param name="createListener">a function to create the underlying listener for this parameter</param>
        /// <returns></returns>
        public static ITriggerBinding GetTriggerBinding<TMessage, TTriggerValue>(
            ITriggerBindingStrategy<TMessage, TTriggerValue> bindingStrategy,
            ParameterInfo parameter,
            IConverterManager converterManager,
            Func<ListenerFactoryContext, bool, Task<IListener>> createListener)
        {
            if (bindingStrategy == null)
            {
                throw new ArgumentNullException("bindingStrategy");
            }
            if (parameter == null)
            {
                throw new ArgumentNullException("parameter");
            }

            bool singleDispatch;
            var argumentBinding = BindingFactoryHelpers.GetTriggerArgumentBinding(bindingStrategy, parameter, converterManager, out singleDispatch);

            var parameterDescriptor = new ParameterDescriptor
            {
                Name = parameter.Name,
                DisplayHints = new ParameterDisplayHints
                {
                    Description = singleDispatch ? "message" : "messages"
                }
            };

            ITriggerBinding binding = new StrategyTriggerBinding<TMessage, TTriggerValue>(
                bindingStrategy, argumentBinding, createListener, parameterDescriptor, singleDispatch);

            return binding;
        }
    }
}