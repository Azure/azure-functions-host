using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class DataBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            IReadOnlyDictionary<string, Type> bindingDataContract = context.BindingDataContract;
            string parameterName = context.Parameter.Name;

            if (bindingDataContract == null || !bindingDataContract.ContainsKey(parameterName))
            {
                return null;
            }

            Type bindingDataType = bindingDataContract[parameterName];
            IBindingProvider typedProvider = CreateTypedBindingProvider(bindingDataType);
            return typedProvider.TryCreate(context);
        }

        private static IBindingProvider CreateTypedBindingProvider(Type bindingDataType)
        {
            Type genericType = typeof(TypedDataBindingProvider<>).MakeGenericType(bindingDataType);
            return (IBindingProvider)Activator.CreateInstance(genericType);
        }

        private class TypedDataBindingProvider<TBindingData> : IBindingProvider
        {
            private static readonly IDataArgumentBindingProvider<TBindingData> _innerProvider =
                new CompositeArgumentBindingProvider<TBindingData>(
                    new ConverterArgumentBindingProvider<TBindingData, TBindingData>(new IdentityConverter<TBindingData>()),
                    new ConverterArgumentBindingProvider<TBindingData, string>(new TToStringConverter<TBindingData>()),
                    new StringToTArgumentBindingProvider<TBindingData>());

            public IBinding TryCreate(BindingProviderContext context)
            {
                ParameterInfo parameter = context.Parameter;

                IArgumentBinding<TBindingData> argumentBinding = _innerProvider.TryCreate(parameter);

                string parameterName = parameter.Name;
                Type parameterType = parameter.ParameterType;

                if (argumentBinding == null)
                {
                    throw new InvalidOperationException(
                        "Can't bind parameter '" + parameterName + "' to type '" + parameterType + "'.");
                }

                return CreateBinding<TBindingData>(parameterType, argumentBinding, parameterName);
            }
        }

        private static IBinding CreateBinding<TBindingData>(Type parameterType, IArgumentBinding<TBindingData> argumentBinding, string parameterName)
        {
            Type genericTypeDefinition;

            if (!parameterType.IsValueType)
            {
                genericTypeDefinition = typeof(ClassDataBinding<,>);
            }
            else
            {
                genericTypeDefinition = typeof(StructDataBinding<,>);
            }

            Type genericType = genericTypeDefinition.MakeGenericType(typeof(TBindingData), parameterType);
            return (IBinding)Activator.CreateInstance(genericType, argumentBinding, parameterName);
        }
    }
}
