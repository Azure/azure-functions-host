using System;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Defines a table that maps a host name to an ID.</summary>
    /// <remarks>
    /// The host GUID serves as an key for lookup purposes, such as for host heartbeats and invocation queues.
    /// </remarks>
    internal interface IHostTable
    {
        Guid GetOrCreateHostId(string hostName);
    }
}
