// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public interface IRpcServer
    {
        /// <summary>
        /// Gets the address that the rpc server is listening on
        /// </summary>
        Uri Uri { get; }

        /// <summary>
        /// Starts a server which will listen for rpc connections
        /// </summary>
        /// <returns>A task that completes when the server is ready</returns>
        Task StartAsync();

        /// <summary>
        /// Forces a server shutdown
        /// </summary>
        /// <returns>A task that completes when the server is killed</returns>
        Task KillAsync();

        /// <summary>
        /// Gracefully shuts down the rpc server, allowing existing calls to finish
        /// </summary>
        /// <returns>A task that completes when the server has shutdown</returns>
        Task ShutdownAsync();
    }
}
