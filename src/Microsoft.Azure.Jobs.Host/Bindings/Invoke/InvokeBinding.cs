using System;

namespace Microsoft.Azure.Jobs.Host.Bindings.Invoke
{
    internal static class InvokeBinding
    {
        public static IBinding Create(string parameterName, Type parameterType)
        {
            if (parameterType.IsByRef)
            {
                return null;
            }

            Type genericTypeDefinition;

            if (!parameterType.IsValueType)
            {
                genericTypeDefinition = typeof(ClassInvokeBinding<>);
            }
            else
            {
                genericTypeDefinition = typeof(StructInvokeBinding<>);
            }

            Type genericType = genericTypeDefinition.MakeGenericType(parameterType);
            return (IBinding)Activator.CreateInstance(genericType, parameterName);
        }
    }
}
