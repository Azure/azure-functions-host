// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    /// <summary>
    /// Provides an abstraction for retrieving worker configuration resolution details.
    /// </summary>
    internal interface IWorkerConfigurationProvider
    {
        /// <summary>
        /// Gets the priority of this configuration provider. Higher value indicate higher priority.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Retrieves a dictionary of worker configurations, keyed by language name.
        /// </summary>
        void PopulateWorkerConfigs(Dictionary<string, RpcWorkerConfig> configs);
    }
}
