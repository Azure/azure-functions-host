using System;
using System.Linq;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal static class CloudBlobStreamObjectBinder
    {
        public static ICloudBlobStreamObjectBinder Create(Type cloudBlobStreamBinderType, out Type valueType)
        {
            Type genericType = GetCloudBlobStreamBinderInterface(cloudBlobStreamBinderType);
            valueType = genericType.GetGenericArguments()[0];
            object innerBinder = Activator.CreateInstance(cloudBlobStreamBinderType);
            Type typelessBinderType = typeof(CloudBlobStreamObjectBinder<>).MakeGenericType(valueType);
            return (ICloudBlobStreamObjectBinder)Activator.CreateInstance(typelessBinderType, innerBinder);
        }

        private static Type GetCloudBlobStreamBinderInterface(Type cloudBlobStreamBinderType)
        {
            return cloudBlobStreamBinderType.GetInterfaces().First(
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICloudBlobStreamBinder<>));
        }
    }
}
