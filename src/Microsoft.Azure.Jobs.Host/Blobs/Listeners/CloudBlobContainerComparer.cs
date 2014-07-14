using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    // CloudBlobContainers are flyweights; distinct references do not equate to distinct containers.
    internal class CloudContainerComparer : IEqualityComparer<CloudBlobContainer>
    {
        public bool Equals(CloudBlobContainer x, CloudBlobContainer y)
        {
            return x.Uri == y.Uri;
        }

        public int GetHashCode(CloudBlobContainer obj)
        {
            return obj.Uri.GetHashCode();
        }
    }
}
