// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers
{
    /// <summary>
    /// WebHost-level singleton that bridges the gRPC layer (where host.json content arrives
    /// via worker capabilities in <c>WorkerInitResponse</c>) and the ScriptHost configuration pipeline.
    /// <para>
    /// In the non-placeholder flow, <see cref="SetContent"/> is called by
    /// <c>WorkerConnectionService</c> after extracting the <c>host_configuration_json</c>
    /// capability. Because <c>WorkerConnectionService</c> blocks in <c>StartAsync</c>,
    /// <see cref="WaitForContent"/> returns immediately when the configuration provider
    /// calls <see cref="ConfigurationProvider.Load"/>.
    /// </para>
    /// </summary>
    internal class HostJsonContentProvider
    {
        private readonly object _lock = new();
        private TaskCompletionSource<string> _tcs = new();
        private string _cachedContent;

        /// <summary>
        /// Stores the host.json content received from the external worker and unblocks
        /// any thread waiting in <see cref="WaitForContent"/>.
        /// </summary>
        /// <param name="hostJsonContent">The raw JSON string of the host.json configuration.</param>
        public void SetContent(string hostJsonContent)
        {
            ArgumentNullException.ThrowIfNull(hostJsonContent);

            lock (_lock)
            {
                _cachedContent = hostJsonContent;
                _tcs.TrySetResult(hostJsonContent);
            }
        }

        /// <summary>
        /// Prepares the provider for a new ScriptHost instance.
        /// </summary>
        /// <param name="clearCache">
        /// When <see langword="false"/> (default), the cached content is preserved so the next
        /// <see cref="WaitForContent"/> call returns immediately — used during ScriptHost restarts
        /// while the worker is still connected.
        /// When <see langword="true"/>, the cached content is cleared — used when the worker
        /// has disconnected and fresh content is expected.
        /// </param>
        public void Reset(bool clearCache = false)
        {
            lock (_lock)
            {
                if (clearCache)
                {
                    _cachedContent = null;
                }

                _tcs = new TaskCompletionSource<string>();

                if (_cachedContent is not null)
                {
                    _tcs.TrySetResult(_cachedContent);
                }
            }
        }

        /// <summary>
        /// Blocks the calling thread until host.json content is available or the timeout expires.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for the content.</param>
        /// <returns>The raw host.json JSON string.</returns>
        /// <exception cref="TimeoutException">
        /// Thrown when no worker provides host.json content within the specified timeout.
        /// </exception>
        public string WaitForContent(TimeSpan timeout)
        {
            Task<string> task;
            lock (_lock)
            {
                task = _tcs.Task;
            }

            if (task.Wait(timeout))
            {
                return task.Result;
            }

            throw new TimeoutException(
                $"No worker provided host.json within {timeout.TotalSeconds} seconds.");
        }
    }
}
