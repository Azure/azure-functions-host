// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host
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
