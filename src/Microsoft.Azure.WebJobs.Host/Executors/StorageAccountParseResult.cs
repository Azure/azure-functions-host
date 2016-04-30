// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    /// <summary>
    /// Enumerates list of possible errors detected by the <see cref="StorageAccountParser"/> while trying to
    /// parse Microsoft Azure Cloud Storage account.
    /// </summary>
    internal enum StorageAccountParseResult
    {
        Success,
        MissingOrEmptyConnectionStringError,
        MalformedConnectionStringError
    }
}
