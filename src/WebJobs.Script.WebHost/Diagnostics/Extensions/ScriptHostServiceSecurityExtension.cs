// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
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
                "{message}");

        public static void BlobStorageSecretRepoError(this ILogger logger, string operation, Exception exception)
        {
            var message = string.Format(Resources.BlobStorageSecretRepositoryFailedOperation, operation);
            _blobStorageSecretRepoError(logger, message, exception);
        }
    }
}
