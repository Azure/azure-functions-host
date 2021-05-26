// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ContainerFunctionExecutionActivityRequest
    {
        public ContainerFunctionExecutionActivityRequest(IEnumerable<ContainerFunctionExecutionActivity> activities)
        {
            Activities = activities;
            FunctionalActivitiesCount = Activities.Count(a => a.IsFunctional());
        }

        public IEnumerable<ContainerFunctionExecutionActivity> Activities { get; }

        public int FunctionalActivitiesCount { get; }
    }
}
