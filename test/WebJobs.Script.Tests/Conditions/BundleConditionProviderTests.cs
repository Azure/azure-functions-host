// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Conditions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Conditions
{
    public class BundleConditionProviderTests
    {
        private readonly TestLogger _logger = new TestLogger("test");
        private readonly TestEnvironment _environment = new TestEnvironment();
        private readonly TestSystemRuntimeInformation _runtimeInfo = new TestSystemRuntimeInformation();

        [Fact]
        public void TryCreateCondition_HostProperty_ReturnsHostPropertyCondition()
        {
            var provider = new BundleConditionProvider(_logger, _environment, _runtimeInfo);
            var descriptor = Descriptor(ConditionConstants.HostPropertyConditionType, "Platform", "^Linux$");

            Assert.True(provider.TryCreateCondition(descriptor, out var condition));
            Assert.IsType<HostPropertyCondition>(condition);
        }

        [Fact]
        public void TryCreateCondition_Environment_ReturnsEnvironmentCondition()
        {
            var provider = new BundleConditionProvider(_logger, _environment, _runtimeInfo);
            var descriptor = Descriptor(ConditionConstants.EnvironmentConditionType, "FOO", "^bar$");

            Assert.True(provider.TryCreateCondition(descriptor, out var condition));
            Assert.IsType<EnvironmentCondition>(condition);
        }

        [Fact]
        public void TryCreateCondition_UnknownType_ReturnsFalseCondition()
        {
            var provider = new BundleConditionProvider(_logger, _environment, _runtimeInfo);
            var descriptor = Descriptor("notARealType", "whatever", "anything");

            Assert.True(provider.TryCreateCondition(descriptor, out var condition));
            Assert.IsType<FalseCondition>(condition);
            Assert.False(condition.Evaluate());
        }

        [Fact]
        public void TryCreateCondition_InvalidRegex_EvaluatesFalse()
        {
            var provider = new BundleConditionProvider(_logger, _environment, _runtimeInfo);
            var descriptor = Descriptor(ConditionConstants.HostPropertyConditionType, "Platform", "[unterminated");

            Assert.True(provider.TryCreateCondition(descriptor, out var condition));
            Assert.False(condition.Evaluate());
        }

        [Fact]
        public void TryCreateCondition_UnknownHostPropertyName_EvaluatesFalse()
        {
            var provider = new BundleConditionProvider(_logger, _environment, _runtimeInfo);
            var descriptor = Descriptor(ConditionConstants.HostPropertyConditionType, "NotARealHostProperty", "anything");

            Assert.True(provider.TryCreateCondition(descriptor, out var condition));
            Assert.False(condition.Evaluate());
        }

        [Fact]
        public void TryCreateCondition_NullDescriptor_ReturnsFalseCondition()
        {
            var provider = new BundleConditionProvider(_logger, _environment, _runtimeInfo);

            Assert.True(provider.TryCreateCondition(null, out var condition));
            Assert.IsType<FalseCondition>(condition);
            Assert.False(condition.Evaluate());
        }

        private static ConditionDescriptor Descriptor(string type, string name, string expression)
        {
            var descriptor = new ConditionDescriptor { Type = type };
            descriptor.Properties[ConditionConstants.ConditionName] = JsonSerializer.SerializeToElement(name);
            descriptor.Properties[ConditionConstants.ConditionExpression] = JsonSerializer.SerializeToElement(expression);
            return descriptor;
        }
    }
}
