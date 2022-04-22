// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Profiles;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal interface IWorkerProfileConditionManager
    {
        bool TryCreateWorkerProfileCondition(WorkerProfileConditionDescriptor conditionDescriptor, out IWorkerProfileCondition condition);
    }
}
