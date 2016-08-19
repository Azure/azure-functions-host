// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace WebJobs.Script.Cli.Common
{
    [Flags]
    internal enum Listable
    {
        None = 0,
        FunctionApps,
        StorageAccounts,
        EventHubs,
        Secrets,
        Tenants,
        Processes,
        Instances,
        RunningHost
    }
}
