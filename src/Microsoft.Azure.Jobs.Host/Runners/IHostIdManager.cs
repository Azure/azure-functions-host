using System;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Defines a manager that maps a shared host name to an ID.</summary>
    /// <remarks>
    /// The host GUID serves as an key for lookup purposes, such as for host heartbeats and invocation queues.
    /// </remarks>
    internal interface IHostIdManager
    {
        Guid GetOrCreateHostId(string sharedHostName);
    }
}
