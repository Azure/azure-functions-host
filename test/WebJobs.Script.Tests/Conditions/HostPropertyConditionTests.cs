// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Conditions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Conditions
{
    public class HostPropertyConditionTests
    {
        private TestSystemRuntimeInformation _testSystemRuntimeInfo = new TestSystemRuntimeInformation();

        // Previously threw ValidationException. The shared condition no longer throws; invalid
        // configurations (including unknown conditionName) leave the condition invalid so
        // Evaluate() returns false — this is the fix for known issue #17 in the design doc.
        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("", null)]
        [InlineData(null, "")]
        [InlineData("sku", null)]
        [InlineData("Platform", "")]
        [InlineData("HostVersion", null)]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "")]
        [InlineData(null, "true")]
        [InlineData("", "true")]
        public void HostPropertyConditionTest_InvalidConfiguration_EvaluatesFalse(string name, string expression)
        {
            var condition = BuildCondition(name, expression);
            Assert.False(condition.Evaluate());
        }

        [Fact]
        public void HostPropertyConditionTest_UnknownConditionName_EvaluatesFalse()
        {
            var condition = BuildCondition("NotARealHostProperty", "anything");
            Assert.False(condition.Evaluate());
        }

        [Fact]
        public void HostPropertyConditionTest_InvalidRegex_EvaluatesFalse()
        {
            var condition = BuildCondition("Platform", "[unterminated");
            Assert.False(condition.Evaluate());
        }

        [Theory]
        [InlineData("platForm", "LINUX")]
        [InlineData("HostVersion", "4.*")]
        public void HostPropertyConditionTest_EvaluateTrue(string name, string testExpression)
        {
            Assert.True(BuildCondition(name, testExpression).Evaluate());
        }

        [Theory]
        [InlineData("platForm", "Windows")]
        [InlineData("HostVersion", "-1")]
        public void HostPropertyConditionTest_EvaluateFalse(string name, string testExpression)
        {
            Assert.False(BuildCondition(name, testExpression).Evaluate(), "Expression evaluates to false");
        }

        private HostPropertyCondition BuildCondition(string name, string expression)
        {
            var descriptor = new ConditionDescriptor
            {
                Type = ConditionConstants.HostPropertyConditionType
            };
            descriptor.Properties[ConditionConstants.ConditionName] = JsonSerializer.SerializeToElement(name);
            descriptor.Properties[ConditionConstants.ConditionExpression] = JsonSerializer.SerializeToElement(expression);
            return new HostPropertyCondition(new TestLogger("test"), _testSystemRuntimeInfo, descriptor);
        }
    }
}
