// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Profiles
{
    public class HostPropertyConditionTests
    {
        private TestSystemRuntimeInformation _testSystemRuntimeInfo = new TestSystemRuntimeInformation();

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
        public void HostPropertyConditionTest_ThrowsValidationException(string name, string expression)
        {
            var testLogger = new TestLogger("test");
            var descriptor = new WorkerProfileConditionDescriptor();
            descriptor.Type = WorkerConstants.WorkerDescriptionProfileHostPropertyCondition;

            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionName] = name;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionExpression] = expression;

            Assert.Throws<ValidationException>(() => new HostPropertyCondition(testLogger, _testSystemRuntimeInfo, descriptor));
        }

        [Theory]
        //[InlineData("sku", "Dynamic")] TODO: Add test case
        [InlineData("platForm", "LINUX")]
        [InlineData("HostVersion", "4.*")]
        public void HostPropertyConditionTest_EvaluateTrue(string name, string testExpression)
        {
            var testLogger = new TestLogger("test");

            var descriptor = new WorkerProfileConditionDescriptor();
            descriptor.Type = WorkerConstants.WorkerDescriptionProfileHostPropertyCondition;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionName] = name;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionExpression] = testExpression;

            var hostPropertyCondition = new HostPropertyCondition(testLogger, _testSystemRuntimeInfo, descriptor);

            Assert.True(hostPropertyCondition.Evaluate());
        }

        [Theory]
        //[InlineData("sku", "Dynamic")] TODO: Add test case
        [InlineData("platForm", "Windows")]
        [InlineData("HostVersion", "-1")]
        public void HostPropertyConditionTest_EvaluateFalse(string name, string testExpression)
        {
            var testLogger = new TestLogger("test");

            var descriptor = new WorkerProfileConditionDescriptor();
            descriptor.Type = WorkerConstants.WorkerDescriptionProfileHostPropertyCondition;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionName] = name;
            descriptor.Properties[WorkerConstants.WorkerDescriptionProfileConditionExpression] = testExpression;

            var hostPropertyCondition = new HostPropertyCondition(testLogger, _testSystemRuntimeInfo, descriptor);

            Assert.False(hostPropertyCondition.Evaluate(), "Expression evaluates to false");
        }
    }
}