// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal static class BlobClient
    {
        public static string GetAccountName(IStorageBlobClient client)
        {
            if (client == null)
            {
                return null;
            }

            return StorageClient.GetAccountName(client.Credentials);
        }

        // Naming rules are here: http://msdn.microsoft.com/en-us/library/dd135715.aspx
        // Validate this on the client side so that we can get a user-friendly error rather than a 400.
        // See code here: http://social.msdn.microsoft.com/Forums/en-GB/windowsazuredata/thread/d364761b-6d9d-4c15-8353-46c6719a3392
        public static void ValidateContainerName(string containerName)
        {
            if (containerName == null)
            {
                throw new ArgumentNullException("containerName");
            }

            if (!IsValidContainerName(containerName))
            {
                throw new FormatException("Invalid container name: " + containerName);
            }
        }

        public static bool IsValidContainerName(string containerName)
        {
            if (containerName == null)
            {
                return false;
            }

            if (containerName.Equals("$root"))
            {
                return true;
            }

            return Regex.IsMatch(containerName, @"^[a-z0-9](([a-z0-9\-[^\-])){1,61}[a-z0-9]$");
        }

        public static void ValidateBlobName(string blobName)
        {
            string errorMessage;

            if (!IsValidBlobName(blobName, out errorMessage))
            {
                throw new FormatException(errorMessage);
            }
        }

        // See http://msdn.microsoft.com/en-us/library/windowsazure/dd135715.aspx.
        // The fun part is that it is not fully correct - the \, [ and ] characters do fail anyway!
        public static bool IsValidBlobName(string blobName, out string errorMessage)
        {
            const string unsafeCharactersMessage =
                "The given blob name '{0}' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.";
            const string tooLongErrorMessage =
                "The given blob name '{0}' is too long. A blob name must be at least one character long and cannot be more than 1,024 characters long.";
            const string tooShortErrorMessage =
                "The given blob name '{0}' is too short. A blob name must be at least one character long and cannot be more than 1,024 characters long.";
            const string invalidSuffixErrorMessage =
                "The given blob name '{0}' has an invalid suffix. Avoid blob names that end with a dot ('.'), a forward slash ('/'), or a sequence or combination of the two.";

            if (blobName == null)
            {
                errorMessage = string.Format(CultureInfo.CurrentCulture, tooShortErrorMessage, String.Empty);
                return false;
            }
            if (blobName.Length == 0)
            {
                errorMessage = string.Format(CultureInfo.CurrentCulture, tooShortErrorMessage, blobName);
                return false;
            }

            if (blobName.Length > 1024)
            {
                errorMessage = string.Format(CultureInfo.CurrentCulture, tooLongErrorMessage, blobName);
                return false;
            }

            if (blobName.EndsWith(".", StringComparison.OrdinalIgnoreCase) || blobName.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = string.Format(CultureInfo.CurrentCulture, invalidSuffixErrorMessage, blobName);
                return false;
            }

            if (blobName.IndexOfAny(UnsafeBlobNameCharacters) > -1)
            {
                errorMessage = string.Format(CultureInfo.CurrentCulture, unsafeCharactersMessage, blobName);
                return false;
            }

            errorMessage = null;
            return true;
        }

        // Tested against storage service on Jan 2014. All other unsafe and reserved characters work fine.
        static readonly char[] UnsafeBlobNameCharacters = { '\\', '[', ']' };
    }
}
