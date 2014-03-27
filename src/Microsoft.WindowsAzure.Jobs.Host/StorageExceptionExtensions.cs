using System;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class StorageExceptionExtensions
    {
        public static bool IsServerSideError(this StorageException exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException("exception");
            }

            if (exception.RequestInformation == null)
            {
                return false;
            }

            int statusCode = exception.RequestInformation.HttpStatusCode;

            return statusCode >= 500 && statusCode < 600;
        }
    }
}
