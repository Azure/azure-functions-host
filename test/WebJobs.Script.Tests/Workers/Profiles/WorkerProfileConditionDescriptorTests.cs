// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Profiles
{
    public class WorkerProfileConditionDescriptorTests
    {
        [Fact]
        public void ConditionDescriptor_ConvertsJObject()
        {
            var conditionJObject = new JsonObject
            {
                [WorkerConstants.WorkerDescriptionProfileConditionName] = WorkerConstants.WorkerDescriptionProfileConditionName,
                [WorkerConstants.WorkerDescriptionProfileConditionExpression] = WorkerConstants.WorkerDescriptionProfileConditionExpression
            };

            Assert.Throws<JsonException>(() => conditionJObject.Deserialize<WorkerProfileConditionDescriptor>());

            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionType] = WorkerConstants.WorkerDescriptionProfileEnvironmentCondition;

            var conditionDescriptor = conditionJObject.Deserialize<WorkerProfileConditionDescriptor>();
            conditionDescriptor.Properties.TryGetValue(WorkerConstants.WorkerDescriptionProfileConditionName, out var conditionDescriptorName);
            conditionDescriptor.Properties.TryGetValue(WorkerConstants.WorkerDescriptionProfileConditionExpression, out var conditionDescriptorExpression);

            Assert.Equal(WorkerConstants.WorkerDescriptionProfileConditionName, conditionDescriptorName.GetString());
            Assert.Equal(WorkerConstants.WorkerDescriptionProfileConditionExpression, conditionDescriptorExpression.GetString());
            Assert.Equal(WorkerConstants.WorkerDescriptionProfileEnvironmentCondition, conditionDescriptor.Type);
        }
    }
}