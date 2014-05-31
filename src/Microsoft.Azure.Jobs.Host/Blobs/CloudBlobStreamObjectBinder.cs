using System;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal static class CloudBlobStreamObjectBinder
    {
        public static ICloudBlobStreamObjectBinder Create(Type cloudBlobStreamBinderType, out Type valueType)
        {
            Type genericType = cloudBlobStreamBinderType.GetGenericTypeDefinition();
            valueType = genericType.GetGenericArguments()[0];
            object innerBinder = Activator.CreateInstance(cloudBlobStreamBinderType);
            Type typelessBinderType = typeof(CloudBlobStreamObjectBinder<>).MakeGenericType(valueType);
            return (ICloudBlobStreamObjectBinder)Activator.CreateInstance(typelessBinderType, innerBinder);
        }
    }
}
