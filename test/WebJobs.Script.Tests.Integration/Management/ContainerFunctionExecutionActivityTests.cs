// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Management
{
    public class ContainerFunctionExecutionActivityTests
    {
        [Fact]
        public void Activity_Equals_Itself()
        {
            var activity = new ContainerFunctionExecutionActivity(DateTime.UtcNow, "func-1", ExecutionStage.InProgress, "QueueTrigger", false);
            Assert.Equal(activity, activity);
        }

        [Fact]
        public void Comparison_Ignores_EventTime()
        {
            var activity1 = new ContainerFunctionExecutionActivity(DateTime.UtcNow, "func-1", ExecutionStage.InProgress, "QueueTrigger", false);
            var activity2 = new ContainerFunctionExecutionActivity(DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(1)), "func-1", ExecutionStage.InProgress, "QueueTrigger", false);

            Assert.Equal(activity1, activity2);
        }

        [Theory]
        [InlineData(true, "", "", ExecutionStage.InProgress, ExecutionStage.InProgress, "", "", false, false)]
        [InlineData(false, "func-1", "", ExecutionStage.InProgress, ExecutionStage.InProgress, "", "", false, false)]
        [InlineData(false, "func-1", "func-1", ExecutionStage.Finished, ExecutionStage.InProgress, "", "", false, false)]
        [InlineData(false, "func-1", "func-1", ExecutionStage.Finished, ExecutionStage.Finished, "trigger-1", "", false, false)]
        [InlineData(false, "func-1", "func-1", ExecutionStage.Finished, ExecutionStage.Finished, "trigger-1", "trigger-1", false, true)]
        public void Comparison_Returns_Expected_Results(bool expected, string functionName1, string functionName2, ExecutionStage stage1, ExecutionStage stage2, string triggerType1, string triggerType2, bool success1, bool success2)
        {
            var activity1 = new ContainerFunctionExecutionActivity(DateTime.MinValue, functionName1, stage1, triggerType1, success1);
            var activity2 = new ContainerFunctionExecutionActivity(DateTime.MaxValue, functionName2, stage2, triggerType2, success2);

            var hashSet = new HashSet<ContainerFunctionExecutionActivity> {activity1, activity2};

            Assert.Equal(expected, activity1.Equals(activity2));
            Assert.Equal(expected ? 1 : 2, hashSet.Count);
        }
    }
}
