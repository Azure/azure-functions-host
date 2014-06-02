using System;
using System.IO;
using System.Threading;

namespace Microsoft.Azure.Jobs
{
    // $$$ Remove this one and merge with IBinder. 
    // Internal one, exposes more details
    internal interface IBinderEx
    {
        BindResult<T> Bind<T>(Attribute attribute);
        string StorageConnectionString { get; }
        CancellationToken CancellationToken { get; }
        Guid FunctionInstanceGuid { get; }
        TextWriter ConsoleOutput { get; }
    }
}
