using System;
using Microsoft.WindowsAzure.Storage;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Provides extension methods for the <see cref="StorageException"/> class.</summary>
#if PUBLICPROTOCOL
    [CLSCompliant(false)]
    public static class StorageExceptionExtensions
#else
    internal static class StorageExceptionExtensions
#endif
    {
        /// <summary>Determines whether the exception is due to a 409 Conflict error.</summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 409 Conflict error; otherwise <see langword="false"/>.
        /// </returns>
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

        /// <summary>Determines whether the exception is due to a 404 Not Found error.</summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 404 Not Found error; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsNotFound(this StorageException exception)
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

        /// <summary>Determines whether the exception is due to a 412 Precondition Failed error.</summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 412 Precondition Failed error; otherwise
        /// <see langword="false"/>.
        /// </returns>
        public static bool IsPreconditionFailed(this StorageException exception)
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

            return result.HttpStatusCode == 412;
        }
    }
}
