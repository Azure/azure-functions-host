// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions
{
    public static class ScriptHostServiceSecurityExtension
    {
        // EventId range is 600-699

        private static readonly Action<ILogger, string, string, Exception> _blobStorageSecretRepoError =
            LoggerMessage.Define<string, string>(
                LogLevel.Error,
                new EventId(600, nameof(BlobStorageSecretRepoError)),
                "There was an error performing a {operation} operation on the Blob Storage Secret Repository. Please ensure the '{appSettingName}' connection string is valid.");

        private static readonly Action<ILogger, string, string, Exception> _blobStorageSecretSasRepoError =
            LoggerMessage.Define<string, string>(
                LogLevel.Error,
                new EventId(601, nameof(BlobStorageSecretSasRepoError)),
                "There was an error performing a {operation} operation on the Blob Storage Secret Repository. Please ensure the '{appSettingName}' SAS URL has Read, Write, and List permissions.");

        public static void BlobStorageSecretRepoError(this ILogger logger, string operation, string appSettingName)
        {
            _blobStorageSecretRepoError(logger, operation, appSettingName, null);
        }

        public static void BlobStorageSecretSasRepoError(this ILogger logger, string operation, string appSettingName)
        {
            _blobStorageSecretSasRepoError(logger, operation, appSettingName, null);
        }
    }
}
