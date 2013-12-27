using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Public one that we bind to. Simpler, doesn't expose a BindResult. 
    public interface IBinder
    {
        T Bind<T>(Attribute attribute);
        string AccountConnectionString { get; }
    }
}
