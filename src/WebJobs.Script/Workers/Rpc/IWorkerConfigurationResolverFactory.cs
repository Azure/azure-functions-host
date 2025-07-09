// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    /// <summary>
    /// Factory interface for creating worker configuration resolvers.
    /// </summary>
    internal interface IWorkerConfigurationResolverFactory
    {
        /// <summary>
        /// Creates an appropriate worker configuration resolver based on the current environment and settings.
        /// </summary>
        /// <returns>An implementation of <see cref="IWorkerConfigurationResolver"/>.</returns>
        IWorkerConfigurationResolver CreateResolver();
    }
}