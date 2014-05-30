using System;

namespace Microsoft.Azure.Jobs
{
    internal interface ICloudTableBinderProvider
    {
        ICloudTableBinder TryGetBinder(Type targetType);
    }
}
