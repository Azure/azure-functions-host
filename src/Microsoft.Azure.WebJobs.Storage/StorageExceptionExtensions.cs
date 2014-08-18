// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage
#else
namespace Microsoft.Azure.WebJobs.Host.Storage
#endif
{
    /// <summary>Provides extension methods for the <see cref="StorageException"/> class.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public static class StorageExceptionExtensions
#else
    internal static class StorageExceptionExtensions
#endif
    {
        /// <summary>
        /// Determines whether the exception is due to a 400 Bad Request error with the error code PopReceiptMismatch.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 400 Bad Request error with the error code
        /// PopReceiptMismatch; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsBadRequestPopReceiptMismatch(this StorageException exception)
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

            if (result.HttpStatusCode != 400)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "PopReceiptMismatch";
        }

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

        /// <summary>
        /// Determines whether the exception is due to a 409 Conflict error with the error code BlobAlreadyExists.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 409 Conflict error with the error code
        /// BlobAlreadyExists; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsConflictBlobAlreadyExists(this StorageException exception)
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

            if (result.HttpStatusCode != 409)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "BlobAlreadyExists";
        }

        /// <summary>
        /// Determines whether the exception is due to a 409 Conflict error with the error code QueueBeingDeleted.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 409 Conflict error with the error code
        /// QueueBeingDeleted; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsConflictQueueBeingDeleted(this StorageException exception)
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

            if (result.HttpStatusCode != 409)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "QueueBeingDeleted";
        }

        /// <summary>
        /// Determines whether the exception is due to a 409 Conflict error with the error code QueueBeingDeleted or
        /// QueueDisabled.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 409 Conflict error with the error code QueueBeingDeleted
        /// or QueueDisabled; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsConflictQueueBeingDeletedOrDisabled(this StorageException exception)
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

            if (result.HttpStatusCode != 409)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "QueueBeingDeleted";
        }

        /// <summary>
        /// Determines whether the exception is due to a 409 Conflict error with the error code QueueDisabled.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 409 Conflict error with the error code QueueDisabled;
        /// otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsConflictQueueDisabled(this StorageException exception)
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

            if (result.HttpStatusCode != 409)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "QueueDisabled";
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

        /// <summary>
        /// Determines whether the exception is due to a 404 Not Found error with the error code BlobNotFound.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 404 Not Found error with the error code BlobNotFound;
        /// otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsNotFoundBlobNotFound(this StorageException exception)
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

            if (result.HttpStatusCode != 404)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "BlobNotFound";
        }

        /// <summary>
        /// Determines whether the exception is due to a 404 Not Found error with the error code ContainerNotFound.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 404 Not Found error with the error code
        /// ContainerNotFound; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsNotFoundContainerNotFound(this StorageException exception)
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

            if (result.HttpStatusCode != 404)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "ContainerNotFound";
        }

        /// <summary>
        /// Determines whether the exception is due to a 404 Not Found error with the error code BlobNotFound or
        /// ContainerNotFound.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 404 Not Found error with the error code BlobNotFound or
        /// ContainerNotFound; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsNotFoundBlobOrContainerNotFound(this StorageException exception)
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

            if (result.HttpStatusCode != 404)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            string errorCode = extendedInformation.ErrorCode;
            return errorCode == "BlobNotFound" || errorCode == "ContainerNotFound";
        }

        /// <summary>
        /// Determines whether the exception is due to a 404 Not Found error with the error code MessageNotFound or
        /// QueueNotFound.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 404 Not Found error with the error code MessageNotFound
        /// or QueueNotFound; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsNotFoundMessageOrQueueNotFound(this StorageException exception)
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

            if (result.HttpStatusCode != 404)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            string errorCode = extendedInformation.ErrorCode;
            return errorCode == "MessageNotFound" || errorCode == "QueueNotFound";
        }

        /// <summary>
        /// Determines whether the exception is due to a 404 Not Found error with the error code MessageNotFound.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 404 Not Found error with the error code MessageNotFound;
        /// otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsNotFoundMessageNotFound(this StorageException exception)
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

            if (result.HttpStatusCode != 404)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "MessageNotFound";
        }

        /// <summary>
        /// Determines whether the exception is due to a 404 Not Found error with the error code QueueNotFound.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 404 Not Found error with the error code QueueNotFound;
        /// otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsNotFoundQueueNotFound(this StorageException exception)
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

            if (result.HttpStatusCode != 404)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "QueueNotFound";
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

        /// <summary>
        /// Determines whether the exception is due to a 412 Precondition Failed error with the error code
        /// ConditionNotMet.
        /// </summary>
        /// <param name="exception">The storage exception.</param>
        /// <returns>
        /// <see langword="true"/> if the exception is due to a 412 Precondition Failed error with the error code
        /// ConditionNotMet; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsPreconditionFailedConditionNotMet(this StorageException exception)
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

            if (result.HttpStatusCode != 412)
            {
                return false;
            }

            StorageExtendedErrorInformation extendedInformation = result.ExtendedErrorInformation;

            if (extendedInformation == null)
            {
                return false;
            }

            return extendedInformation.ErrorCode == "ConditionNotMet";
        }
    }
}
