// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    /// <summary>
    /// Provides an abstraction for retrieving worker configurations.
    /// </summary>
    internal interface IWorkerConfigurationResolver
    {
        /// <summary>
        /// Retrieves a dictionary of worker configurations, keyed by language name.
        /// </summary>
        IReadOnlyDictionary<string, RpcWorkerConfig> GetWorkerConfigs();
    }
}
