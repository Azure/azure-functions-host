// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.WebJobs.Script.Conditions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Conditions
{
    public class ConditionDescriptorTests
    {
        [Fact]
        public void ConditionDescriptor_DeserializesExtensionData()
        {
            var conditionJObject = new JsonObject
            {
                [ConditionConstants.ConditionName] = ConditionConstants.ConditionName,
                [ConditionConstants.ConditionExpression] = ConditionConstants.ConditionExpression
            };

            Assert.Throws<JsonException>(() => conditionJObject.Deserialize<ConditionDescriptor>());

            conditionJObject[ConditionConstants.ConditionType] = ConditionConstants.EnvironmentConditionType;

            var descriptor = conditionJObject.Deserialize<ConditionDescriptor>();
            descriptor.Properties.TryGetValue(ConditionConstants.ConditionName, out var name);
            descriptor.Properties.TryGetValue(ConditionConstants.ConditionExpression, out var expression);

            Assert.Equal(ConditionConstants.ConditionName, name.GetString());
            Assert.Equal(ConditionConstants.ConditionExpression, expression.GetString());
            Assert.Equal(ConditionConstants.EnvironmentConditionType, descriptor.Type);
        }
    }
}
