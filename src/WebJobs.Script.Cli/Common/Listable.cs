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
