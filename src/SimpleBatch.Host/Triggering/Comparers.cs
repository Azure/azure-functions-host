using System.Collections.Generic;
using Microsoft.WindowsAzure.StorageClient;

// Resolve with dups in RunnerInterfaces
namespace Microsoft.WindowsAzure.Jobs
{
    // CloudBlobContainers are flyweights, may not compare. 
    internal class CloudBlobClientComparer : IEqualityComparer<CloudBlobClient>
    {
        public bool Equals(CloudBlobClient x, CloudBlobClient y)
        {
            return x.Credentials.AccountName == y.Credentials.AccountName;
        }

        public int GetHashCode(CloudBlobClient obj)
        {
            return obj.Credentials.AccountName.GetHashCode();
        }
    }
}
