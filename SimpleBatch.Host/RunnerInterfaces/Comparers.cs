using System.Collections.Generic;
using System.Reflection;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    // CloudBlobContainers are flyweights, may not compare. 
    public class CloudContainerComparer : IEqualityComparer<CloudBlobContainer>
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
    public class CloudQueueComparer : IEqualityComparer<CloudQueue>
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

    public class AssemblyNameComparer : IEqualityComparer<AssemblyName>
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