using System;
using Microsoft.WindowsAzure.Storage;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    internal static class StorageExceptionExtensions
    {
        public static bool IsConflict(this StorageException exception)
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

            return result.HttpStatusCode == 409;
        }

        public static bool IsFileNotFound(this StorageException exception)
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

            return result.HttpStatusCode == 404;
        }
    }
}
