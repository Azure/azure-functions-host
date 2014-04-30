using System;

namespace Microsoft.Azure.Jobs
{
    // Binder for any arbitrary azure things. Could even bind to multiple things. 
    // No meta infromation here, so we can't reason anything about it (Reader, writer, etc)
    internal interface ICloudBinderProvider
    {
        ICloudBinder TryGetBinder(Type targetType);
    }
}
