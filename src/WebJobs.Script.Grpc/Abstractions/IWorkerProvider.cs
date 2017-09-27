// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    /// <summary>
    /// Enables a language worker implementor to specify how to create and configure the language worker process.
    /// </summary>
    public interface IWorkerProvider
    {
        /// <summary>
        /// Get the static description of the worker.
        /// </summary>
        /// <returns>The static description of the worker.</returns>
        WorkerDescription GetDescription();

        /// <summary>
        /// Tries to configure the arguments with any configuration / environment specific settings.
        /// </summary>
        /// <param name="args">The default arguments constructed by the host.</param>
        /// <param name="config">The host-level IConfiguration.</param>
        /// <param name="logger">The startup ILogger.</param>
        /// <returns>A bool that indicates if the args were configured successfully.</returns>
        bool TryConfigureArguments(ArgumentsDescription args, IConfiguration config, ILogger logger);
    }
}
