using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IConfiguration
    {
        // Could cache a wrapper directly binding against IClourBlobBinder.
        IList<ICloudBlobBinderProvider> BlobBinders { get; }

        IList<ICloudTableBinderProvider> TableBinders { get; }

        // General type binding. 
        IList<ICloudBinderProvider> Binders { get; }
    }
}
