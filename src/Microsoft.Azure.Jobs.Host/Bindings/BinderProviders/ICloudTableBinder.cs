using System;

namespace Microsoft.Azure.Jobs
{
    internal interface ICloudTableBinder
    {
        BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName);
    }
}
