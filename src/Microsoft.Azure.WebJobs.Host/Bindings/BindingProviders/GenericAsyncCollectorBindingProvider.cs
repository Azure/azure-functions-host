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
    internal class GenericAsyncCollectorBindingProvider<TAttribute, TConstructorArg> :
        IBindingProvider
        where TAttribute : Attribute
    {
        private readonly INameResolver _nameResolver;
        private readonly IConverterManager _converterManager;
        private readonly Type _asyncCollectorType;
        private readonly Func<TAttribute, TConstructorArg> _constructorParameterBuilder;

        public GenericAsyncCollectorBindingProvider(
            INameResolver nameResolver,
            IConverterManager converterManager,
            Type asyncCollectorType,
            Func<TAttribute, TConstructorArg> constructorParameterBuilder)
        {
            this._nameResolver = nameResolver;
            this._converterManager = converterManager;
            this._asyncCollectorType = asyncCollectorType;
            this._constructorParameterBuilder = constructorParameterBuilder;
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

            var wrapper = WrapperBase.New(
                typeMessage, _asyncCollectorType, _constructorParameterBuilder, _nameResolver, _converterManager, parameter);

            IBinding binding = wrapper.CreateBinding();
            return Task.FromResult(binding);
        }

        // Wrappers to help with binding to a dynamically typed IAsyncCollector<T>. 
        // TMessage is not known until runtime, so we need to dynamically create it. 
        // These inherit the generic args of the outer class. 
        private abstract class WrapperBase
        {
            protected Func<TAttribute, TConstructorArg> ConstructorParameterBuilder { get; private set; }
            protected INameResolver NameResolver { get; private set; }
            protected IConverterManager ConverterManager { get; private set; }
            protected ParameterInfo Parameter { get; private set; }
            protected Type AsyncCollectorType { get; private set; }

            public abstract IBinding CreateBinding();

            internal static WrapperBase New(
                Type typeMessage,
                Type asyncCollectorType,
                Func<TAttribute, TConstructorArg> constructorParameterBuilder,
                INameResolver nameResolver,
                IConverterManager converterManager,
                ParameterInfo parameter)
            {
                // These inherit the generic args of the outer class. 
                var t = typeof(Wrapper<>).MakeGenericType(typeof(TAttribute), typeof(TConstructorArg), typeMessage);
                var obj = Activator.CreateInstance(t);
                var obj2 = (WrapperBase)obj;

                obj2.ConstructorParameterBuilder = constructorParameterBuilder;
                obj2.NameResolver = nameResolver;
                obj2.ConverterManager = converterManager;
                obj2.Parameter = parameter;
                obj2.AsyncCollectorType = asyncCollectorType;

                return obj2;
            }
        }

        private class Wrapper<TMessage> : WrapperBase
        {
            // This is the builder function that gets passed to the core IAsyncCollector binders. 
            public IAsyncCollector<TMessage> BuildFromAttribute(TAttribute attribute)
            {
                // Dynmically invoke this:
                //   TConstructorArg ctorArg = _buildFromAttr(attribute);
                //   IAsyncCollector<TMessage> collector = new MyCollector<TMessage>(ctorArg);

                var ctorArg = ConstructorParameterBuilder(attribute);

                var t = AsyncCollectorType.MakeGenericType(typeof(TMessage));
                var obj = Activator.CreateInstance(t, ctorArg);
                var collector = (IAsyncCollector<TMessage>)obj;
                return collector;
            }

            public override IBinding CreateBinding()
            {
                IBinding binding = BindingFactoryHelpers.BindCollector<TAttribute, TMessage>(
                Parameter,
                NameResolver,
                ConverterManager,
                this.BuildFromAttribute, 
                null);

                return binding;
            }
        }
    } // end class 
}