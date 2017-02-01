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
    /// Helper class for creating some generally useful BindingProviders
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
        /// Creating a type filter predicate around another binding provider. Filter doubles as a validator if it throws.  
        /// </summary>
        /// <param name="predicate">A predication function to determine whether to use the corresponding inner provider. 
        /// The predicate is called once at indexing time and passed a non-resolved attribute. If it returns true, the inner provider is used to bind this parameter. It can throw an exception to signal an indexing error. </param>
        /// <param name="innerProvider">Inner provider to use if the predicate returns true.</param>
        /// <returns>A new binding provider that wraps the existing provider with a predicate.</returns>
        public IBindingProvider AddFilter<TAttribute>(Func<TAttribute, Type, bool> predicate, IBindingProvider innerProvider)
            where TAttribute : Attribute
        {
            return new FilteringBindingProvider<TAttribute>(predicate, this._nameResolver, innerProvider);
        }

        /// <summary>
        /// Creating a type filter predicate around another binding provider. 
        /// </summary>
        /// <param name="predicate">type predicate. Only apply inner provider if this predicate as applied to the user parameter type is true. </param>
        /// <param name="innerProvider">Inner provider to use if the predicate returns true.</param>
        /// <returns>A new binding provider that wraps the existing provider with a predicate.</returns>
        public IBindingProvider AddFilter(Func<Type, bool> predicate, IBindingProvider innerProvider)
        {
            return new FilteringBindingProvider<Attribute>((attr, parameterType) => predicate(parameterType), this._nameResolver, innerProvider);
        }

        /// <summary>
        /// Creating a validation predicate around another binding provider. 
        /// The predicate is only run if the inner binding is applied. 
        /// </summary>
        /// <param name="validator">a validator function to invoke on the attribute during indexing. This is called at most once, 
        /// and only if the inner provider returns a binding. </param>
        /// <param name="innerProvider">Inner provider. This is always run.  </param>
        /// <returns>A new binding provider that wraps the existing provider with a validator.</returns>
        public IBindingProvider AddValidator<TAttribute>(Action<TAttribute, Type> validator, IBindingProvider innerProvider)
            where TAttribute : Attribute
        {
            return new ValidatingWrapperBindingProvider<TAttribute>(validator, this._nameResolver, innerProvider);
        }

        /// <summary>
        /// Create a binding provider that returns an IValueBinder from a resolved attribute. IValueBinder will let you have an OnCompleted hook that 
        /// is invoked after the user function completes. 
        /// </summary>
        /// <typeparam name="TAttribute">Type of binding attribute on the user's parameter.</typeparam>
        /// <param name="builder">Builder function to create a IValueBinder given a resolved attribute and the user parameter type. </param>
        /// <returns>A binding provider that applies these semantics.</returns>
        public IBindingProvider BindToGenericValueProvider<TAttribute>(Func<TAttribute, Type, Task<IValueBinder>> builder)
            where TAttribute : Attribute
        {
            return new ItemBindingProvider<TAttribute>(this._nameResolver, builder);
        }

        /// <summary>
        /// Create a binding provider for binding a parameter to an <see cref="IAsyncCollector{TMEssage}"/>. 
        /// Use the <see cref="IConverterManager"/> to convert form the user's parameter type to the TMessage type. 
        /// </summary>
        /// <typeparam name="TAttribute">Type of binding attribute on the user's parameter.</typeparam>
        /// <typeparam name="TMessage">element type of the IAsyncCollector.</typeparam>
        /// <param name="buildFromAttribute">Function to allocate the collector object given a resolved instance of the attribute.</param>
        /// <returns>A binding provider that applies these semantics.</returns>
        public IBindingProvider BindToCollector<TAttribute, TMessage>(
            Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute)
            where TAttribute : Attribute
        {
            var converter = new DelegateAdapterCollectorBuilder<TAttribute, TMessage> { BuildFromAttribute = buildFromAttribute };
            var pm = PatternMatcher.New(converter);
            return new AsyncCollectorBindingProvider<TAttribute, TMessage>(this._nameResolver, this._converterManager, pm);
        }

        /// <summary>
        /// Create a binding provider for binding a parameter to an <see cref="IAsyncCollector{TType}"/>. 
        /// </summary>
        /// <typeparam name="TAttribute">Type of binding attribute on the user's parameter.</typeparam>
        /// <typeparam name="TType">'core type' for the IAsyncCollector. This can be an OpenType and allow resolving against generics.</typeparam>
        /// <param name="builderInstance">builder object that converts from the attribute to an AsyncCollector. </param>
        /// <returns></returns>
        public IBindingProvider BindToCollector<TAttribute, TType>(            
            IConverter<TAttribute, IAsyncCollector<TType>> builderInstance)
            where TAttribute : Attribute
        {
            var pm = PatternMatcher.New(builderInstance);
            return new AsyncCollectorBindingProvider<TAttribute, TType>(this._nameResolver, this._converterManager, pm);
        }

        /// <summary>
        /// Create a binding provider for binding a parameter to an <see cref="IAsyncCollector{TType}"/>. 
        /// </summary>
        /// <typeparam name="TAttribute">Type of binding attribute on the user's parameter.</typeparam>
        /// <typeparam name="TType">'core type' for the IAsyncCollector. This can be an OpenType and allow resolving against generics.</typeparam>
        /// <param name="builderType">
        /// Type of the builder object. Should expose a conversion from TAttribute to <see cref="IAsyncCollector{TType}"/>
        /// </param>
        /// <param name="constructorArgs">Arguments to pass to the constructor for the builderType.</param>
        /// <returns></returns>
        public IBindingProvider BindToCollector<TAttribute, TType>(
            Type builderType,
            params object[] constructorArgs)
            where TAttribute : Attribute
        {
            var pm = PatternMatcher.New(builderType, constructorArgs);
            return new AsyncCollectorBindingProvider<TAttribute, TType>(this._nameResolver, this._converterManager, pm);
        }

        /// <summary>
        /// General rule for binding to an generic input type for a given attribute. 
        /// </summary>
        /// <typeparam name="TAttribute">Type of binding attribute on the user's parameter.</typeparam>
        /// <typeparam name="TType">The user type must be compatible with this type for the binding to apply.</typeparam>
        /// <param name="builderType">A that implements IConverter for the target parameter. 
        /// This will get instantiated with the appropriate generic args to perform the builder rule.</param>
        /// <param name="constructorArgs">constructor arguments to pass to the typeBuilder instantiation. This can be used 
        /// to flow state (like configuration, secrets, etc) from the configuration to the specific binding</param>
        /// <returns>A binding rule.</returns>
        public IBindingProvider BindToInput<TAttribute, TType>(
            Type builderType,
            params object[] constructorArgs)
                where TAttribute : Attribute
        {
            var pm = PatternMatcher.New(builderType, constructorArgs);
            return new BindToInputBindingProvider<TAttribute, TType>(this._nameResolver, this._converterManager, pm);
        }

        /// <summary>
        /// General rule for binding to an concrete input type for a given attribute. 
        /// </summary>
        /// <typeparam name="TAttribute">Type of binding attribute on the user's parameter.</typeparam>
        /// <typeparam name="TType">The user type must be compatible with this type for the binding to apply.</typeparam>
        /// <param name="builderInstance">Instance with converter methods on it.</param>
        /// <returns>A binding rule.</returns>
        public IBindingProvider BindToInput<TAttribute, TType>(
            IConverter<TAttribute, TType> builderInstance)
            where TAttribute : Attribute
        {
            var pm = PatternMatcher.New(builderInstance);
            return new BindToInputBindingProvider<TAttribute, TType>(this._nameResolver, this._converterManager, pm);
        }

        /// <summary>
        /// Bind a  parameter to an IAsyncCollector. Use this for things that have discrete output items (like sending messages or writing table rows)
        /// This will add additional adapters to connect the user's parameter type to an IAsyncCollector. 
        /// </summary>
        /// <typeparam name="TMessage">The 'core type' for the IAsyncCollector.</typeparam>
        /// <typeparam name="TTriggerValue">The type of the trigger object to pass to the listener.</typeparam>
        /// <param name="bindingStrategy">A strategy object that describes how to do the binding</param>
        /// <param name="parameter">The user's parameter being bound to</param>
        /// <param name="converterManager">The converter manager, used to convert between the user parameter's type and the underlying native types used by the trigger strategy</param>
        /// <param name="createListener">A function to create the underlying listener for this parameter</param>
        /// <returns>A trigger binding</returns>
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

        // Adapter to expose a delegate veneer over IAsyncCollector builders. 
        // Delegates are more convenient for concrete types. 
        internal class DelegateAdapterCollectorBuilder<TAttribute, TMessage> : IConverter<TAttribute, IAsyncCollector<TMessage>>
            where TAttribute : Attribute
        {
            public Func<TAttribute, IAsyncCollector<TMessage>> BuildFromAttribute { get; set; }

            public IAsyncCollector<TMessage> Convert(TAttribute input)
            {
                var result = BuildFromAttribute(input);
                return result;
            }
        }
    }
}