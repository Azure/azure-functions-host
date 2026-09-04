// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Conditions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Conditions
{
    public class EnvironmentConditionTests
    {
        private TestEnvironment _testEnvironment = new TestEnvironment();

        // Previously threw ValidationException. The shared condition no longer throws; invalid
        // configurations leave the condition in a "not valid" state where Evaluate() returns false.
        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("", null)]
        [InlineData(null, "")]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", null)]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "")]
        [InlineData(null, "true")]
        [InlineData("", "true")]
        public void EnvironmentConditionTest_InvalidConfiguration_EvaluatesFalse(string name, string expression)
        {
            var condition = BuildCondition(name, expression);
            Assert.False(condition.Evaluate());
        }

        [Fact]
        public void EnvironmentConditionTest_InvalidRegex_EvaluatesFalse()
        {
            var condition = BuildCondition("APPLICATIONINSIGHTS_ENABLE_AGENT", "[unterminated");
            Assert.False(condition.Evaluate());
        }

        [Theory]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "true", "true")]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "^((?!true).)*$", "false")]
        public void EnvironmentConditionTest_EvaluateTrue(string name, string testExpression, string environmentSetting)
        {
            _testEnvironment.SetEnvironmentVariable(name, environmentSetting);
            Assert.True(BuildCondition(name, testExpression).Evaluate());
        }

        [Theory]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "true", "false")]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "^((?!true).)*$", "true")]
        public void EnvironmentConditionTest_EvaluateFalse(string name, string testExpression, string environmentSetting)
        {
            _testEnvironment.SetEnvironmentVariable(name, environmentSetting);
            Assert.False(BuildCondition(name, testExpression).Evaluate(), "Expression evaluates to false");
        }

        private EnvironmentCondition BuildCondition(string name, string expression)
        {
            var descriptor = new ConditionDescriptor
            {
                Type = ConditionConstants.EnvironmentConditionType
            };
            descriptor.Properties[ConditionConstants.ConditionName] = JsonSerializer.SerializeToElement(name);
            descriptor.Properties[ConditionConstants.ConditionExpression] = JsonSerializer.SerializeToElement(expression);
            return new EnvironmentCondition(new TestLogger("test"), _testEnvironment, descriptor);
        }
    }
}
