using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Binder for any arbitrary azure things. Could even bind to multiple things. 
    // No meta infromation here, so we can't reason anything about it (Reader, writer, etc)
    internal interface ICloudBinderProvider
    {
        ICloudBinder TryGetBinder(Type targetType);
    }
}
