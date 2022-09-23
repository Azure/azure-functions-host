// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Profiles
{
    public class EnvironmentConditionTests
    {
        private TestEnvironment _testEnvironment = new TestEnvironment();

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("", null)]
        [InlineData(null, "")]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", null)]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "")]
        [InlineData(null, "true")]
        [InlineData("", "true")]
        public void EnvironmentConditionTest_ThrowsValidationException(string name, string expression)
        {
            var testLogger = new TestLogger("test");
            var descriptor = new WorkerProfileConditionDescriptor();
            descriptor.Type = WorkerConstants.WorkerDescriptionProfileEnvironmentCondition;

            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionName] = name;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionExpression] = expression;

            Assert.Throws<ValidationException>(() => new EnvironmentCondition(testLogger, _testEnvironment, descriptor));
        }

        [Theory]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "true", "true")]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "^((?!true).)*$", "false")]
        public void EnvironmentConditionTest_EvaluateTrue(string name, string testExpression, string environmentSetting)
        {
            _testEnvironment.SetEnvironmentVariable(name, environmentSetting);

            var testLogger = new TestLogger("test");

            var descriptor = new WorkerProfileConditionDescriptor();
            descriptor.Type = WorkerConstants.WorkerDescriptionProfileEnvironmentCondition;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionName] = name;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionExpression] = testExpression;

            var environmentCondition = new EnvironmentCondition(testLogger, _testEnvironment, descriptor);

            Assert.True(environmentCondition.Evaluate());
        }

        [Theory]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "true", "false")]
        [InlineData("APPLICATIONINSIGHTS_ENABLE_AGENT", "^((?!true).)*$", "true")]
        public void EnvironmentConditionTest_EvaluateFalse(string name, string testExpression, string environmentSetting)
        {
            _testEnvironment.SetEnvironmentVariable(name, environmentSetting);

            var testLogger = new TestLogger("test");

            var descriptor = new WorkerProfileConditionDescriptor();
            descriptor.Type = WorkerConstants.WorkerDescriptionProfileEnvironmentCondition;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionName] = name;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionExpression] = testExpression;

            var environmentCondition = new EnvironmentCondition(testLogger, _testEnvironment, descriptor);

            Assert.False(environmentCondition.Evaluate(), "Expression evaluates to false");
        }
    }
}