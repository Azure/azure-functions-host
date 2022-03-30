// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions
{
    public static class ScriptHostServiceSecurityExtension
    {
        // EventId range is 600-699
        private static readonly Action<ILogger, string, Exception> _blobStorageSecretRepoError =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(600, nameof(BlobStorageSecretRepoError)),
                "There was an error performing a {operation} operation on the Blob Storage Secret Repository.");

        public static void BlobStorageSecretRepoError(this ILogger logger, string operation, Exception exception)
        {
            _blobStorageSecretRepoError(logger, operation, exception);
        }
    }
}
