using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // $$$ Remove this one and merge with IBinder. 
    // Internal one, exposes the BindResult.
    internal interface IBinderEx
    {
        BindResult<T> Bind<T>(Attribute attribute);
        string AccountConnectionString { get; }
        Guid FunctionInstanceGuid { get; }
    }
}
