// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
        MalformedConnectionStringError,
        EmulatorIsNotSupportedError
    }
}
