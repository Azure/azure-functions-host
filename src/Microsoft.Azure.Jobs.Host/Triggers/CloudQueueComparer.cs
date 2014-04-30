using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs
{
    // CloudQueue are flyweights, may not compare. 
    internal class CloudQueueComparer : IEqualityComparer<CloudQueue>
    {
        public bool Equals(CloudQueue x, CloudQueue y)
        {
            return x.Uri == y.Uri;
        }

        public int GetHashCode(CloudQueue obj)
        {
            return obj.Uri.GetHashCode();
        }
    }
}
