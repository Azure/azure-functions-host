using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface ICloudTableBinder
    {
        BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName);
    }
}
