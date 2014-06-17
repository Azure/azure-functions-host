using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Blobs.Triggers
{
    internal static class BlobPathSource
    {
        public static IBlobPathSource Create(string pattern)
        {
            if (pattern == null)
            {
                throw new FormatException("Blob paths must not be null.");
            }

            string containerNamePattern;
            string blobNamePattern;
            int slashIndex = pattern.IndexOf('/');
            bool hasBlobName = slashIndex != -1;

            if (hasBlobName)
            {
                // There must be at least one character before the slash and one character after the slash.
                bool hasNonEmptyBlobAndContainerNames = slashIndex > 0 && slashIndex < pattern.Length - 1;

                if (!hasNonEmptyBlobAndContainerNames)
                {
                    throw new FormatException("Blob paths must be in the format container/blob.");
                }

                containerNamePattern = pattern.Substring(0, slashIndex);
                blobNamePattern = pattern.Substring(slashIndex + 1);
            }
            else
            {
                containerNamePattern = pattern;
                blobNamePattern = String.Empty;
            }

            List<string> parameterNames = new List<string>();

            BindingDataPath.AddParameterNames(containerNamePattern, parameterNames);
            BindingDataPath.AddParameterNames(blobNamePattern, parameterNames);

            if (parameterNames.Count > 0)
            {
                return new ParameterizedBlobPathSource(containerNamePattern, blobNamePattern, parameterNames);
            }

            BlobClient.ValidateContainerName(containerNamePattern);

            if (hasBlobName)
            {
                BlobClient.ValidateBlobName(blobNamePattern);
            }

            return new FixedBlobPathSource(new BlobPath(containerNamePattern, blobNamePattern));
        }
    }
}
