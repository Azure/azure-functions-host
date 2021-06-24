// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public class RpcWorkerConfig
    {
        internal static readonly TimeSpan DefaultInitializationTimeout = TimeSpan.FromSeconds(10);

        public RpcWorkerDescription Description { get; set; }

        public WorkerProcessArguments Arguments { get; set; }

        public WorkerProcessCountOptions CountOptions { get; set; }

        /// <summary>
        /// Gets or sets the process startup timeout. This is the time from when the process is
        /// launched to when the StartStream message is received.
        /// </summary>
        public TimeSpan ProcessStartupTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Gets or sets the worker initialization timeout. This is the time from when the WorkerInitRequest
        /// is sent to when the WorkerInitResponse is received.
        /// </summary>
        public TimeSpan InitializationTimeout { get; set; } = DefaultInitializationTimeout;

        /// <summary>
        /// Gets or sets the worker environment reload timeout. This is the time from when the FunctionEnvironmentReloadRequest
        /// is sent to when the FunctionEnvironmentReloadResponse is received.
        /// </summary>
        public TimeSpan EnvironmentReloadTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets interval between process restarts. Default 10secs
        /// </summary>
        public TimeSpan ProcessRestartInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets the process shutdown timeout. Default 10secs
        /// </summary>
        public TimeSpan ProcessShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }
}
