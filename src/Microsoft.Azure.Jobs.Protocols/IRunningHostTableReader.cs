using System;

namespace Microsoft.Azure.Jobs.Protocols
{
    /// <summary>Defines a reader providing running host heartbeats.</summary>
    [CLSCompliant(false)]
    public interface IRunningHostTableReader
    {
        /// <summary>Reads all running host heartbeats.</summary>
        /// <returns>All running host heartbeats.</returns>
        RunningHost[] ReadAll();

        /// <summary>Reads a host or host instance heartbeat.</summary>
        /// <param name="hostOrInstanceId">The ID of the host or host instance.</param>
        /// <returns>The heartbeat, if any.</returns>
        DateTimeOffset? Read(Guid hostOrInstanceId);
    }
}
