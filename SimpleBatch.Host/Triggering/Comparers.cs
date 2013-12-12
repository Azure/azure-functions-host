using System.Collections.Generic;
using System.Reflection;
using Microsoft.WindowsAzure.StorageClient;

// Resolve with dups in RunnerInterfaces
namespace TriggerService
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

    // CloudBlobContainers are flyweights, may not compare. 
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

    internal class AssemblyNameComparer : IEqualityComparer<AssemblyName>
    {
        public bool Equals(AssemblyName x, AssemblyName y)
        {
            return x.ToString() == y.ToString();
        }

        public int GetHashCode(AssemblyName obj)
        {
            return obj.ToString().GetHashCode();
        }
    }
}