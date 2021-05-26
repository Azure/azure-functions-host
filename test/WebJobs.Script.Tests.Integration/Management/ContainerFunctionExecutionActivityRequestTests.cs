// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    public class ContainerFunctionExecutionActivityRequestTests
    {
        private const string FunctionName = "HttpTrigger1";
        private const string TriggerType = "HttpTrigger";

        [Fact]
        public void HandlesActivitiesListWithNoFunctionalActivities()
        {
            var containerFunctionExecutionActivities = new List<ContainerFunctionExecutionActivity>();
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Started, true));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Started, false));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Finished, false));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Failed, true));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Failed, false));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Succeeded, true));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Succeeded, false));

            var request = new ContainerFunctionExecutionActivityRequest(containerFunctionExecutionActivities);
            Assert.Equal(7, request.Activities.Count());
            Assert.Equal(0, request.FunctionalActivitiesCount);
        }

        [Fact]
        public void ReturnsFunctionalActivitiesCount()
        {
            var containerFunctionExecutionActivities = new List<ContainerFunctionExecutionActivity>();
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Started, true));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Started, false));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Finished, false));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Failed, true));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Failed, false));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Succeeded, true));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Succeeded, false));

            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.InProgress, true));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.InProgress, false));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.InProgress, false));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Finished, true));
            containerFunctionExecutionActivities.Add(GetActivity(ExecutionStage.Finished, true));

            var request = new ContainerFunctionExecutionActivityRequest(containerFunctionExecutionActivities);
            Assert.Equal(12, request.Activities.Count());
            Assert.Equal(5, request.FunctionalActivitiesCount);
        }

        [Fact]
        public void HandlesEmptyActivitiesList()
        {
            var request =
                new ContainerFunctionExecutionActivityRequest(Enumerable.Empty<ContainerFunctionExecutionActivity>());
            Assert.Empty(request.Activities);
            Assert.Equal(0, request.FunctionalActivitiesCount);
        }

        private static ContainerFunctionExecutionActivity GetActivity(ExecutionStage executionStage, bool success)
        {
            return new ContainerFunctionExecutionActivity(DateTime.UtcNow, FunctionName, executionStage, TriggerType,
                success);
        }
    }
}
