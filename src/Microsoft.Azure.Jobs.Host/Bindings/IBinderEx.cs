using System;
using System.IO;
using System.Threading;

namespace Microsoft.Azure.Jobs
{
    // $$$ Remove this one and merge with IBinder. 
    // Internal one, exposes more details
    internal interface IBinderEx
    {
        string StorageConnectionString { get; }
        CancellationToken CancellationToken { get; }
        TextWriter ConsoleOutput { get; }
    }
}
