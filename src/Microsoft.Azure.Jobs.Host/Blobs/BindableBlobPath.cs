using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal static class BindableBlobPath
    {
        public static IBindableBlobPath Create(string pattern)
        {
            BlobPath parsedPattern = BlobPath.Parse(pattern);
            string containerNamePattern = parsedPattern.ContainerName;
            string blobNamePattern = parsedPattern.BlobName;

            List<string> parameterNames = new List<string>();

            BindingDataPath.AddParameterNames(containerNamePattern, parameterNames);
            BindingDataPath.AddParameterNames(blobNamePattern, parameterNames);

            if (parameterNames.Count > 0)
            {
                return new ParameterizedBlobPath(containerNamePattern, blobNamePattern, parameterNames);
            }

            BlobClient.ValidateContainerName(containerNamePattern);
            BlobClient.ValidateBlobName(blobNamePattern);
            return new BoundBlobPath(parsedPattern);
        }
    }
}
