// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    // Regulate profile operations through the profile manager
    public interface IWorkerProfileManager
    {
        /// <summary>
        /// Creates profile condition using different condition descriptor properties
        /// </summary>
        bool TryCreateWorkerProfileCondition(WorkerProfileConditionDescriptor conditionDescriptor, out IWorkerProfileCondition condition);

        /// <summary>
        /// Save different profiles for a given worker runtime language
        /// </summary>
        void SaveWorkerDescriptionProfiles(List<WorkerDescriptionProfile> workerDescriptionProfiles, string language);

        /// <summary>
        /// Load a profile that meets it's conditions
        /// </summary>
        void LoadWorkerDescriptionFromProfiles(RpcWorkerDescription defaultWorkerDescription, out RpcWorkerDescription workerDescription);

        /// <summary>
        /// Verify if the current profile's conditions have changed
        /// </summary>
        bool IsCorrectProfileLoaded(string workerRuntime);
    }
}
