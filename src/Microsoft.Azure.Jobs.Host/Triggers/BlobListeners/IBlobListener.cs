using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    internal interface IBlobListener
    {
        // Scan the container
        // Callbacks may fire multiple times. Or out of order relative to creation date. 
        void Poll(Action<ICloudBlob, HostBindingContext> callback, HostBindingContext context);
    }
}
