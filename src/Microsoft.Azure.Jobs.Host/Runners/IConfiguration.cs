using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    internal interface IConfiguration
    {
        // Could cache a wrapper directly binding against ICloudBlobBinder.
        IList<ICloudBlobBinderProvider> BlobBinders { get; }

        IList<ICloudTableBinderProvider> TableBinders { get; }

        INameResolver NameResolver { get; set; }
    }
}
