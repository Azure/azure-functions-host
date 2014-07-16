// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.Jobs.Host
{
    // Names of containers used only by hosts (not directly part of the protocol with the dashboard, though other parts
    // may point to blobs stored here).
    internal static class HostContainerNames
    {
        public const string Hosts = "azure-jobs-hosts";
    }
}
