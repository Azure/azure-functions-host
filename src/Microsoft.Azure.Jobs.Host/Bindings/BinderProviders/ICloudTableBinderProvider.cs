using System;

namespace Microsoft.Azure.Jobs
{
    internal interface ICloudTableBinderProvider
    {
        // isReadOnly - True if we know we want it read only (must have been specified in the attribute).
        ICloudTableBinder TryGetBinder(Type targetType, bool isReadOnly);
    }
}
