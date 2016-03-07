// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    internal class NullHeadbeatValidityMonitor : IHeartbeatValidityMonitor
    {
        public bool IsInstanceHeartbeatValid(string sharedContainerName, string sharedDirectoryName, string instanceBlobName, int expirationInSeconds)
        {
            return true;
        }

        public bool IsSharedHeartbeatValid(string sharedContainerName, string sharedDirectoryName, int expirationInSeconds)
        {
            return true;
        }
    }
}