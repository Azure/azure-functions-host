// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Profiles
{
    public class WorkerProfileConditionDescriptorTests
    {
        [Fact]
        public void ConditionDescriptor_ConvertsJObject()
        {
            var conditionJObject = new JObject();
            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionName] = WorkerConstants.WorkerDescriptionProfileConditionName;
            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionExpression] = WorkerConstants.WorkerDescriptionProfileConditionExpression;

            Assert.Throws<JsonSerializationException>(() => conditionJObject.ToObject<WorkerProfileConditionDescriptor>());

            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionType] = WorkerConstants.WorkerDescriptionProfileEnvironmentCondition;

            var conditionDescriptor = conditionJObject.ToObject<WorkerProfileConditionDescriptor>();
            conditionDescriptor.Properties.TryGetValue(WorkerConstants.WorkerDescriptionProfileConditionName, out string conditionDescriptorName);
            conditionDescriptor.Properties.TryGetValue(WorkerConstants.WorkerDescriptionProfileConditionExpression, out string conditionDescriptorExpression);

            Assert.Equal(WorkerConstants.WorkerDescriptionProfileConditionName, conditionDescriptorName);
            Assert.Equal(WorkerConstants.WorkerDescriptionProfileConditionExpression, conditionDescriptorExpression);
            Assert.Equal(WorkerConstants.WorkerDescriptionProfileEnvironmentCondition, conditionDescriptor.Type);
        }
    }
}