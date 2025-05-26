// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    /// <summary>
    /// Interface to resolve WorkerConfigs using configured probing paths and fallback path.
    /// </summary>
    /// <param name="probingPaths">List of probing paths where workers are located.</param>
    /// <param name="fallback">A fallback path to check when workers cannot be found in the probing paths.</param>
    /// <returns> A list of paths to worker configuration files. </returns>
    public interface IWorkerConfigurationResolver
    {
        internal List<string> GetWorkerConfigs(List<string> probingPaths, string fallbackPath);
    }
}