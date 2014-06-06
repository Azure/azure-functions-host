using System;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    internal static class StorageExceptionExtensions
    {
        public static bool IsServerSideError(this StorageException exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException("exception");
            }

            RequestResult result = exception.RequestInformation;

            if (result == null)
            {
                return false;
            }

            int statusCode = result.HttpStatusCode;
            return statusCode >= 500 && statusCode < 600;
        }
    }
}
