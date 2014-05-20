using System;
using System.IO;
using System.Threading;

namespace Microsoft.Azure.Jobs
{
    // $$$ Remove this one and merge with IBinder. 
    // Internal one, exposes the BindResult, NotifyNewBlob and ConsoleOutput.
    internal interface IBinderEx
    {
        BindResult<T> Bind<T>(Attribute attribute);
        string StorageConnectionString { get; }
        CancellationToken CancellationToken { get; }
        Guid FunctionInstanceGuid { get; }
        INotifyNewBlob NotifyNewBlob { get; }
        TextWriter ConsoleOutput { get; }
    }
}
