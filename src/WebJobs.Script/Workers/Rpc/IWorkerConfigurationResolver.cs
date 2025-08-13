// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    /// <summary>
    /// Provides an abstraction for retrieving worker configuration resolution details.
    /// </summary>
    internal interface IWorkerConfigurationResolver
    {
        /// <summary>
        /// Retrieves the worker configuration resolution information which includes the root directory path of workers and worker configuration paths.
        /// </summary>
        WorkerConfigurationInfo GetConfigurationInfo();
    }
}
