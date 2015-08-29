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
            string result = _containerName;
            if (!string.IsNullOrEmpty(_blobName))
            {
                result += "/" + _blobName;
            }

            return result;
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

        public static BlobPath Parse(string value, bool isContainerBinding)
        {
            BlobPath path;

            if (!TryParse(value, isContainerBinding, out path))
            {
                throw new FormatException("Blob identifiers must be in the format 'container/blob'.");
            }

            return path;
        }

        public static bool TryParse(string value, bool isContainerBinding, out BlobPath path)
        {
            path = null;

            if (value == null)
            {
                return false;
            }

            int slashIndex = value.IndexOf('/');
            if (!isContainerBinding && slashIndex <= 0)
            {
                return false;
            }

            if (slashIndex > 0 && slashIndex == value.Length - 1)
            {
                // if there is a slash present, there must be at least one character before
                // the slash and one character after the slash.
                return false;
            }

            string containerName = slashIndex > 0 ? value.Substring(0, slashIndex) : value;
            string blobName = slashIndex > 0 ? value.Substring(slashIndex + 1) : string.Empty;

            path = new BlobPath(containerName, blobName);
            return true;
        }

        private static bool TryParseAndValidate(string value, out string errorMessage, out BlobPath path)
        {
            BlobPath possiblePath;

            if (!TryParse(value, false, out possiblePath))
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
