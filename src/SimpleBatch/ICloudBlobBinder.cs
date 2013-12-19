using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Bind a CloudBlob to something more structured.
    // Use CloudBlob instead of Stream so that we have metadata, filename.
    internal interface ICloudBlobBinder
    {
        // Returned object should be assignable to target type.
        BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType);
    }
}
