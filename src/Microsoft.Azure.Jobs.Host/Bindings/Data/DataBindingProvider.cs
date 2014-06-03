using System;
using System.Collections.Generic;
using System.IO;
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
            private static readonly ITypeToObjectConverter<TBindingData>[] _converters = new ITypeToObjectConverter<TBindingData>[]
            {
                new InputConverter<TBindingData, TBindingData>(new IdentityConverter<TBindingData>()),
                new InputConverter<TBindingData, string>(new TToStringConverter<TBindingData>()),
                new StringToTTypeToObjectConverter<TBindingData>()
            };

            public IBinding TryCreate(BindingProviderContext context)
            {
                ITypeToObjectConverter<TBindingData> converter = null;
                ParameterInfo parameter = context.Parameter;
                Type parameterType = parameter.ParameterType;

                foreach (ITypeToObjectConverter<TBindingData> possibleConverter in _converters)
                {
                    if (possibleConverter.CanConvert(parameterType))
                    {
                        converter = possibleConverter;
                        break;
                    }
                }

                string parameterName = parameter.Name;

                if (converter == null)
                {
                    throw new InvalidOperationException(
                        "Can't bind parameter '" + parameterName + "' to type '" + parameterType + "'.");
                }

                IArgumentBinding<TBindingData> argumentBinding = new DataArgumentBinding<TBindingData>(converter, parameterType);

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
