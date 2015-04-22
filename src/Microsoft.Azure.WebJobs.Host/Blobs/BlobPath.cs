// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class BlobPath
    {
        private readonly string _containerName;
        private readonly string _blobName;

        public BlobPath(string containerName, string blobName)
        {
            if (containerName == null)
            {
                throw new ArgumentNullException("containerName");
            }

            if (blobName == null)
            {
                throw new ArgumentNullException("blobName");
            }

            _containerName = containerName;
            _blobName = blobName;
        }

        public string ContainerName
        {
            get { return _containerName; }
        }

        public string BlobName
        {
            get { return _blobName; }
        }

        public override string ToString()
        {
            return _containerName + "/" + _blobName;
        }

        public static BlobPath ParseAndValidate(string value)
        {
            string errorMessage;
            BlobPath path;

            if (!TryParseAndValidate(value, out errorMessage, out path))
            {
                throw new FormatException(errorMessage);
            }

            return path;
        }

        public static BlobPath Parse(string value)
        {
            BlobPath path;

            if (!TryParse(value, out path))
            {
                throw new FormatException("Blob identifiers must be in the format 'container/blob'.");
            }

            return path;
        }

        public static bool TryParse(string value, out BlobPath path)
        {
            if (value == null)
            {
                path = null;
                return false;
            }

            int slashIndex = value.IndexOf('/');

            // There must be at least one character before the slash and one character after the slash.
            if (slashIndex <= 0 || slashIndex == value.Length - 1)
            {
                path = null;
                return false;
            }

            string containerName = value.Substring(0, slashIndex);
            string blobName = value.Substring(slashIndex + 1);

            path = new BlobPath(containerName, blobName);
            return true;
        }

        private static bool TryParseAndValidate(string value, out string errorMessage, out BlobPath path)
        {
            BlobPath possiblePath;

            if (!TryParse(value, out possiblePath))
            {
                errorMessage = "Blob identifiers must be in the format 'container/blob'.";
                path = null;
                return false;
            }

            if (!BlobClient.IsValidContainerName(possiblePath.ContainerName))
            {
                errorMessage = "Invalid container name: " + possiblePath.ContainerName;
                path = null;
                return false;
            }

            string possibleErrorMessage;

            if (!BlobClient.IsValidBlobName(possiblePath.BlobName, out possibleErrorMessage))
            {
                errorMessage = possibleErrorMessage;
                path = null;
                return false;
            }

            errorMessage = null;
            path = possiblePath;
            return true;
        }
    }
}
