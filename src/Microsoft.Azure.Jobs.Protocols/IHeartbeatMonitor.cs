namespace Microsoft.Azure.Jobs.Protocols
{
    /// <summary>Defines a monitor for running host heartbeats.</summary>
    public interface IHeartbeatMonitor
    {
        /// <summary>Determines if at least one instance heartbeat is valid for a host.</summary>
        /// <param name="sharedContainerName">
        /// The name of the heartbeat container shared by all instances of the host.
        /// </param>
        /// <param name="sharedDirectoryName">
        /// The name of the directory in <paramref name="sharedContainerName"/> shared by all instances of the host.
        /// </param>
        /// <param name="expirationInSeconds">The number of seconds after the heartbeat that it expires.</param>
        /// <returns>
        /// <see langword="true"/> if at least one instance heartbeat is valid for the host; otherwise,
        /// <see langword="false"/>.
        /// </returns>
        bool IsSharedHeartbeatValid(string sharedContainerName, string sharedDirectoryName, int expirationInSeconds);

        /// <summary>Determines if a host instance has a valid heartbeat.</summary>
        /// <param name="sharedContainerName">
        /// The name of the heartbeat container shared by all instances of the host.
        /// </param>
        /// <param name="sharedDirectoryName">
        /// The name of the directory in <paramref name="sharedContainerName"/> shared by all instances of the host.
        /// </param>
        /// <param name="instanceBlobName">
        /// The name of the host instance heartbeat blob in <paramref name="sharedDirectoryName"/>.
        /// </param>
        /// <param name="expirationInSeconds">The number of seconds after the heartbeat that it expires.</param>
        /// <returns>
        /// <see langword="true"/> if the host instance has a valid heartbeat; otherwise, <see langword="false"/>.
        /// </returns>
        bool IsInstanceHeartbeatValid(string sharedContainerName, string sharedDirectoryName, string instanceBlobName,
            int expirationInSeconds);
    }
}
