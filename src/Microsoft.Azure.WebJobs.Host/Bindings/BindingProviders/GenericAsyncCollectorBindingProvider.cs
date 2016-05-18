// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Bind Attribute --> IAsyncCollector<TMessage>, where TMessage is determined by the  user parameter type.
    // This skips the converter manager and instead dynamically allocates a generic IAsyncCollector<TMessage>
    // where TMessage matches the user parameter type. 
    internal class GenericAsyncCollectorBindingProvider<TAttribute> :
        IBindingProvider
        where TAttribute : Attribute
    {
        private readonly INameResolver _nameResolver;
        private readonly Func<TAttribute, Type, object> _builder;
        private readonly Func<TAttribute, Type, bool> _filter;

        public GenericAsyncCollectorBindingProvider(
            INameResolver nameResolver,
            Func<TAttribute, Type, object> builder,
            Func<TAttribute, Type, bool> filter)
        {
            this._nameResolver = nameResolver;
            this._builder = builder;
            this._filter = filter ?? DefaultFilter;          
        }

        private static bool DefaultFilter(TAttribute attribute, Type messageType)
        {
            return true;
        }

        // Called once per method definition. Very static. 
        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            ParameterInfo parameter = context.Parameter;
            TAttribute attribute = parameter.GetCustomAttribute<TAttribute>(inherit: false);

            if (attribute == null)
            {
                return Task.FromResult<IBinding>(null);
            }

            // Now we can instantiate against the user's type.
            // throws if can't infer the type. 
            Type typeMessage = TypeUtility.GetMessageTypeFromAsyncCollector(parameter.ParameterType);

            // Apply filter 
            var cloner = new AttributeCloner<TAttribute>(attribute, _nameResolver);
            var attrNameResolved = cloner.GetNameResolvedAttribute();
            bool canUse = _filter(attrNameResolved, typeMessage);

            if (!canUse)
            {
                return Task.FromResult<IBinding>(null);
            }

            var wrapper = WrapperBase.New(
                typeMessage, _builder, _nameResolver, parameter);

            IBinding binding = wrapper.CreateBinding();
            return Task.FromResult(binding);
        }

        // Wrappers to help with binding to a dynamically typed IAsyncCollector<T>. 
        // TMessage is not known until runtime, so we need to dynamically create it. 
        // These inherit the generic args of the outer class. 
        private abstract class WrapperBase
        {
            protected Func<TAttribute, Type, object> Builder { get; set; }
            protected INameResolver NameResolver { get; private set; }
            protected ParameterInfo Parameter { get; private set; }
            protected Type AsyncCollectorType { get; private set; }

            public abstract IBinding CreateBinding();

            internal static WrapperBase New(
                Type typeMessage,
                Func<TAttribute, Type, object> builder,
                INameResolver nameResolver,
                ParameterInfo parameter)
            {
                // These inherit the generic args of the outer class. 
                var t = typeof(Wrapper<>).MakeGenericType(typeof(TAttribute), typeMessage);
                var obj = Activator.CreateInstance(t);
                var obj2 = (WrapperBase)obj;

                obj2.Builder = builder;
                obj2.NameResolver = nameResolver;
                obj2.Parameter = parameter;
                return obj2;
            }
        }

        private class Wrapper<TMessage> : WrapperBase
        {
            // This is the builder function that gets passed to the core IAsyncCollector binders. 
            public IAsyncCollector<TMessage> BuildFromAttribute(TAttribute attribute)
            {
                var obj = this.Builder(attribute, typeof(TMessage));                
                var collector = (IAsyncCollector<TMessage>)obj;
                return collector;
            }

            public override IBinding CreateBinding()
            {
                IBinding binding = BindingFactoryHelpers.BindCollector<TAttribute, TMessage>(
                    Parameter,
                    NameResolver,
                    new IdentityConverterManager(),
                    this.BuildFromAttribute, 
                    null);

                return binding;
            }
        }

        // "Empty" converter manager that only allows identity conversions. 
        // The GenericAsyncCollector is already instantiated against the user type, so no conversions should be needed. 
        private class IdentityConverterManager : IConverterManager
        {
            public void AddConverter<TSource, TDestination>(Func<TSource, TDestination> converter)
            {
                throw new NotImplementedException();
            }

            public void AddConverter<TSource, TDestination, TAttribute1>(Func<TSource, TAttribute1, TDestination> converter) where TAttribute1 : Attribute
            {
                throw new NotImplementedException();
            }

            public Func<TSource, TAttribute1, TDestination> GetConverter<TSource, TDestination, TAttribute1>() where TAttribute1 : Attribute
            {
                if (typeof(TSource) != typeof(TDestination))
                {
                    return null;
                }
                return (src, attr) =>
                {
                    object obj = (object)src;
                    return (TDestination)obj;
                };
            }
        }
    } // end class 
}