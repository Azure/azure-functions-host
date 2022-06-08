// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.Profiles
{
    /// <summary>
    /// Interface to manage different profile condition providers
    /// </summary>
    internal interface IWorkerProfileConditionProvider
    {
        /// <summary>
        /// Factory method to create a profile condition
        /// </summary>
        bool TryCreateCondition(WorkerProfileConditionDescriptor descriptor, out IWorkerProfileCondition condition);
    }
}
