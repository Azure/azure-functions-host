using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Azure.Jobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    internal static class BlobClient
    {
        public static string GetAccountName(CloudBlobClient client)
        {
            if (client == null)
            {
                return null;
            }

            return StorageClient.GetAccountName(client.Credentials);
        }

        public static DateTime? GetBlobModifiedUtcTime(ICloudBlob blob)
        {
            if (!blob.Exists())
            {
                return null; // no blob, no time.
            }

            var props = blob.Properties;
            var time = props.LastModified;
            return time.HasValue ? (DateTime?)time.Value.UtcDateTime : null;
        }

        // Return Null if doesn't exist
        [DebuggerNonUserCode]
        public static string ReadBlob(ICloudBlob blob)
        {
            // Beware! Blob.DownloadText does not strip the BOM! 
            try
            {
                using (var stream = blob.OpenRead())
                using (StreamReader sr = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
                {
                    string data = sr.ReadToEnd();
                    return data;
                }
            }
            catch
            {
                return null;
            }
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
                errorMessage = string.Format(tooShortErrorMessage, String.Empty);
                return false;
            }

            if (blobName.Length == 0)
            {
                errorMessage = string.Format(tooShortErrorMessage, blobName);
                return false;
            }

            if (blobName.Length > 1024)
            {
                errorMessage = string.Format(tooLongErrorMessage, blobName);
                return false;
            }

            if (blobName.EndsWith(".") || blobName.EndsWith("/"))
            {
                errorMessage = string.Format(invalidSuffixErrorMessage, blobName);
                return false;
            }

            if (blobName.IndexOfAny(UnsafeBlobNameCharacters) > -1)
            {
                errorMessage = string.Format(unsafeCharactersMessage, blobName);
                return false;
            }

            errorMessage = null;
            return true;
        }

        // Tested against storage service on Jan 2014. All other unsafe and reserved characters work fine.
        static readonly char[] UnsafeBlobNameCharacters = { '\\', '[', ']' };
    }
}
