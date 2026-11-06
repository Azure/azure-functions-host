// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;
using FuncDescriptor = Microsoft.Azure.WebJobs.Host.Protocols.FunctionDescriptor;

namespace Microsoft.Azure.WebJobs.Script.Description.Tests
{
    public class FunctionGroupListenerDecoratorTests
    {
        private readonly ILogger<FunctionGroupListenerDecorator> _logger
            = NullLogger<FunctionGroupListenerDecorator>.Instance;

        private enum FuncGroup
        {
            None,
            Http,
            Other,
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void Decorate_DiscoveryAssignmentSuppressesAllListeners(bool isDiscoveryPod, bool expectedSuppressed)
        {
            IFunctionDefinition definition = Mock.Of<IFunctionDefinition>();
            IListener original = Mock.Of<IListener>();
            IFunctionMetadataManager metadata = Mock.Of<IFunctionMetadataManager>();
            IEnvironment environment = CreateEnvironment(null);

            var context = new ListenerDecoratorContext(definition, original.GetType(), original);
            var decorator = new FunctionGroupListenerDecorator(metadata, environment, CreateAssignmentState(isDiscoveryPod), _logger);

            IListener result = decorator.Decorate(context);

            Assert.Equal(expectedSuppressed, !ReferenceEquals(context.Listener, result));
        }

        [Fact]
        public void Decorate_NoTargetGroupConfigured_ReturnsOriginalListener()
        {
            // Arrange
            IFunctionDefinition definition = Mock.Of<IFunctionDefinition>();
            IListener original = Mock.Of<IListener>();
            IFunctionMetadataManager metadata = Mock.Of<IFunctionMetadataManager>();
            IEnvironment environment = CreateEnvironment(null);

            var context = new ListenerDecoratorContext(definition, original.GetType(), original);
            var decorator = new FunctionGroupListenerDecorator(metadata, environment, CreateAssignmentState(), _logger);

            // Act
            var result = decorator.Decorate(context);

            // Assert
            Assert.Same(context.Listener, result);
        }

        [Fact]
        public void Decorate_MetadataNotFound_ReturnsOriginalListener()
        {
            // Arrange
            IFunctionDefinition definition = CreateDefinition("test");
            IListener original = Mock.Of<IListener>();
            IFunctionMetadataManager metadata = Mock.Of<IFunctionMetadataManager>();
            IEnvironment environment = CreateEnvironment("http");

            var context = new ListenerDecoratorContext(definition, original.GetType(), original);
            var decorator = new FunctionGroupListenerDecorator(metadata, environment, CreateAssignmentState(), _logger);

            // Act
            var result = decorator.Decorate(context);

            // Assert
            Assert.Same(context.Listener, result);
        }

        [Fact]
        public void Decorate_GroupMatch_ReturnsOriginalListener()
        {
            // Arrange
            IFunctionDefinition definition = CreateDefinition("test");
            IListener original = Mock.Of<IListener>();
            IFunctionMetadataManager metadata = CreateMetadataManager("test", true);
            IEnvironment environment = CreateEnvironment("http");

            var context = new ListenerDecoratorContext(definition, original.GetType(), original);
            var decorator = new FunctionGroupListenerDecorator(metadata, environment, CreateAssignmentState(), _logger);

            // Act
            var result = decorator.Decorate(context);

            // Assert
            Assert.Same(context.Listener, result);
        }

        [Fact]
        public void Decorate_GroupDoesNotMatch_ReturnsNoOpListener()
        {
            // Arrange
            IFunctionDefinition definition = CreateDefinition("test");
            IListener original = Mock.Of<IListener>();
            IFunctionMetadataManager metadata = CreateMetadataManager("test", true);
            IEnvironment environment = CreateEnvironment("other");

            var context = new ListenerDecoratorContext(definition, original.GetType(), original);
            var decorator = new FunctionGroupListenerDecorator(metadata, environment, CreateAssignmentState(), _logger);

            // Act
            var result = decorator.Decorate(context);

            // Assert
            Assert.NotSame(context.Listener, result);
        }

        [Theory]
        [InlineData("http")]
        [InlineData("blob")]
        public void Decorate_BlobTrigger_EnabledForGroup(string targetGroup)
        {
            // Arrange
            IFunctionDefinition definition = CreateDefinition("test");
            IListener original = Mock.Of<IListener>();

            var metadata = new FunctionMetadata()
            {
                Name = "TestFunction1",
                Bindings =
                {
                    new BindingMetadata
                    {
                        Name = "input",
                        Type = "blobTrigger",
                        Direction = BindingDirection.In,
                        Raw = new JObject()
                        {
                            ["name"] = "input",
                            ["type"] = "blobTrigger",
                            ["direction"] = "in",
                            ["source"] = "EventGrid",
                        },
                    }
                }
            };

            var metadataMock = new Mock<IFunctionMetadataManager>();
            metadataMock.Setup(p => p.TryGetFunctionMetadata("test", out metadata, false)).Returns(true);

            IEnvironment environment = CreateEnvironment(targetGroup);

            var context = new ListenerDecoratorContext(definition, original.GetType(), original);
            var decorator = new FunctionGroupListenerDecorator(metadataMock.Object, environment, CreateAssignmentState(), _logger);

            // Act
            var result = decorator.Decorate(context);

            // Assert
            Assert.Same(context.Listener, result);
        }

        private static IFunctionDefinition CreateDefinition(string name)
        {
            var descriptor = new FuncDescriptor { LogName = name };
            return Mock.Of<IFunctionDefinition>(m => m.Descriptor == descriptor);
        }

        private static IFunctionMetadataManager CreateMetadataManager(string name, bool group)
        {
            string trigger = group ? "httpTrigger" : "otherTrigger";
            var metadata = new FunctionMetadata()
            {
                Name = "TestFunction1",
                Bindings =
                {
                    new BindingMetadata
                    {
                        Name = "input",
                        Type = trigger,
                        Direction = BindingDirection.In,
                        Raw = new JObject()
                        {
                            ["name"] = "input",
                            ["type"] = trigger,
                            ["direction"] = "in",
                        },
                    }
                }
            };

            var mock = new Mock<IFunctionMetadataManager>();
            mock.Setup(p => p.TryGetFunctionMetadata(name, out metadata, false)).Returns(true);
            return mock.Object;
        }

        private static IEnvironment CreateEnvironment(string group)
        {
            var environment = new Mock<IEnvironment>(MockBehavior.Strict);
            environment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsTargetGroup)).Returns(group);
            return environment.Object;
        }

        private static IHostAssignmentState CreateAssignmentState(bool isDiscoveryPod = false)
        {
            return Mock.Of<IHostAssignmentState>(state => state.IsDiscoveryPod == isDiscoveryPod);
        }
    }
}
