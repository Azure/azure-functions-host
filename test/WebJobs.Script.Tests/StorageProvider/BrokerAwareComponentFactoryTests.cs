// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.StorageProvider
{
    public class BrokerAwareComponentFactoryTests
    {
        private readonly Mock<AzureComponentFactory> _innerFactory;
        private readonly BrokerAwareComponentFactory _factory;

        public BrokerAwareComponentFactoryTests()
        {
            _innerFactory = new Mock<AzureComponentFactory>();
            _factory = new BrokerAwareComponentFactory(_innerFactory.Object);
        }

        [Fact]
        public void Constructor_NullInner_ThrowsArgumentNullException()
        {
            Action act = () => new BrokerAwareComponentFactory(null);
            act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("inner");
        }

        [Fact]
        public void CreateTokenCredential_BrokerCredential_ReturnsBrokerTokenCredential()
        {
            var config = BuildConfiguration(
                ("credential", "Broker"),
                ("brokerEndpoint", "http://broker.test"),
                ("brokerApiKey", "test-key"));

            var credential = _factory.CreateTokenCredential(config);

            credential.Should().BeOfType<BrokerTokenCredential>();
            _innerFactory.Verify(f => f.CreateTokenCredential(It.IsAny<IConfiguration>()), Times.Never);
        }

        [Fact]
        public void CreateTokenCredential_BrokerCredential_CaseInsensitive()
        {
            var config = BuildConfiguration(
                ("credential", "broker"),
                ("brokerEndpoint", "http://broker.test"),
                ("brokerApiKey", "test-key"));

            var credential = _factory.CreateTokenCredential(config);

            credential.Should().BeOfType<BrokerTokenCredential>();
        }

        [Fact]
        public void CreateTokenCredential_BrokerCredential_MissingEndpoint_ThrowsInvalidOperationException()
        {
            var config = BuildConfiguration(
                ("credential", "Broker"),
                ("brokerApiKey", "test-key"));

            Action act = () => _factory.CreateTokenCredential(config);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*brokerEndpoint*");
        }

        [Fact]
        public void CreateTokenCredential_BrokerCredential_MissingApiKey_ThrowsInvalidOperationException()
        {
            var config = BuildConfiguration(
                ("credential", "Broker"),
                ("brokerEndpoint", "http://broker.test"));

            Action act = () => _factory.CreateTokenCredential(config);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*brokerApiKey*");
        }

        [Fact]
        public void CreateTokenCredential_StorageProxyCredential_ReturnsStorageProxyTokenCredential()
        {
            var config = BuildConfiguration(
                ("credential", "StorageProxy"),
                ("brokerApiKey", "test-key"));

            var credential = _factory.CreateTokenCredential(config);

            credential.Should().BeOfType<StorageProxyTokenCredential>();
            _innerFactory.Verify(f => f.CreateTokenCredential(It.IsAny<IConfiguration>()), Times.Never);
        }

        [Fact]
        public void CreateTokenCredential_StorageProxyCredential_CaseInsensitive()
        {
            var config = BuildConfiguration(
                ("credential", "storageproxy"),
                ("brokerApiKey", "test-key"));

            var credential = _factory.CreateTokenCredential(config);

            credential.Should().BeOfType<StorageProxyTokenCredential>();
        }

        [Fact]
        public void CreateTokenCredential_StorageProxyCredential_MissingApiKey_ThrowsInvalidOperationException()
        {
            var config = BuildConfiguration(
                ("credential", "StorageProxy"));

            Action act = () => _factory.CreateTokenCredential(config);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*brokerApiKey*");
        }

        [Fact]
        public void CreateTokenCredential_NonBrokerCredential_DelegatesToInner()
        {
            var expectedCredential = new Mock<TokenCredential>().Object;
            var config = BuildConfiguration(("credential", "ManagedIdentity"));

            _innerFactory.Setup(f => f.CreateTokenCredential(config)).Returns(expectedCredential);

            var credential = _factory.CreateTokenCredential(config);

            credential.Should().BeSameAs(expectedCredential);
            _innerFactory.Verify(f => f.CreateTokenCredential(config), Times.Once);
        }

        [Fact]
        public void CreateTokenCredential_NoCredentialKey_DelegatesToInner()
        {
            var expectedCredential = new Mock<TokenCredential>().Object;
            var config = BuildConfiguration();

            _innerFactory.Setup(f => f.CreateTokenCredential(config)).Returns(expectedCredential);

            var credential = _factory.CreateTokenCredential(config);

            credential.Should().BeSameAs(expectedCredential);
        }

        [Fact]
        public void CreateClientOptions_DelegatesToInner()
        {
            var optionsType = typeof(object);
            var config = BuildConfiguration();
            var expectedOptions = new object();

            _innerFactory.Setup(f => f.CreateClientOptions(optionsType, null, config)).Returns(expectedOptions);

            var options = _factory.CreateClientOptions(optionsType, null, config);

            options.Should().BeSameAs(expectedOptions);
        }

        [Fact]
        public void CreateClient_DelegatesToInner()
        {
            var clientType = typeof(object);
            var config = BuildConfiguration();
            var credential = new Mock<TokenCredential>().Object;
            var clientOptions = new object();
            var expectedClient = new object();

            _innerFactory.Setup(f => f.CreateClient(clientType, config, credential, clientOptions)).Returns(expectedClient);

            var client = _factory.CreateClient(clientType, config, credential, clientOptions);

            client.Should().BeSameAs(expectedClient);
        }

        private static IConfiguration BuildConfiguration(params (string key, string value)[] values)
        {
            var builder = new ConfigurationBuilder();
            var data = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var (key, value) in values)
            {
                data[key] = value;
            }

            builder.AddInMemoryCollection(data);
            return builder.Build();
        }
    }
}
