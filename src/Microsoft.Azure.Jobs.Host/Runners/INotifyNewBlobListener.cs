using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs
{
    // Listen on new blobs, invoke a callback when they're detected.
    // This is a fast-path form of blob listening. 
    // ### Can this be merged with the other general blob listener or IBlobListener?     
    internal interface INotifyNewBlobListener
    {
        void ProcessMessages(Action<BlobWrittenMessage, HostBindingContext> fpOnNewBlob, HostBindingContext context);
    }
}
