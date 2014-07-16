// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host
{
    // Names of directories used only by hosts (not directly part of the protocol with the dashboard, though other parts
    // may point to blobs stored here).
    internal static class HostDirectoryNames
    {
        public const string Heartbeats = "heartbeats";

        public const string Ids = "ids";

        public const string OutputLogs = "output-logs";
    }
}
