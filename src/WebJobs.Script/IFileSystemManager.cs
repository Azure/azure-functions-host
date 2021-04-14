// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IFileSystemManager
    {
        void CacheIfBlobExists(ILogger logger);

        bool IsFileSystemReadOnly(ILogger logger);

        bool IsZipDeployment(ILogger logger, bool validate = true);
    }
}