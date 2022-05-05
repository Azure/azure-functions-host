// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public interface IWorkerProfileManager
    {
        bool TryCreateWorkerProfileCondition(WorkerProfileConditionDescriptor conditionDescriptor, out IWorkerProfileCondition condition);

        void SaveWorkerDescriptionProfiles(List<WorkerDescriptionProfile> workerDescriptionProfiles, string language);

        void LoadWorkerDescriptionFromProfiles(RpcWorkerDescription defaultWorkerDescription, out RpcWorkerDescription workerDescription);

        bool IsCorrectProfileLoaded(string workerRuntime);
    }
}
