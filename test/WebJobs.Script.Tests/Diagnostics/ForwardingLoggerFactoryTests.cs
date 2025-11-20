// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class ForwardingLoggerFactoryTests
    {
        private readonly Mock<ILoggerFactory> _mockInnerFactory;
        private readonly Mock<IScriptHostManager> _mockManager;
        private readonly Mock<ILogger> _mockInnerLogger;
        private readonly ForwardingLoggerFactory _factory;

        public ForwardingLoggerFactoryTests()
        {
            _mockInnerFactory = new Mock<ILoggerFactory>();
            _mockManager = new Mock<IScriptHostManager>();
            _mockInnerLogger = new Mock<ILogger>();
            _factory = new ForwardingLoggerFactory(_mockInnerFactory.Object, _mockManager.Object);
        }

        [Fact]
        public void Constructor_WithNullInnerFactory_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new ForwardingLoggerFactory(null, Mock.Of<IScriptHostManager>()))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("inner");
        }

        [Fact]
        public void Constructor_WithNullManager_ThrowsArgumentNullException()
        {
            TestHelpers.Act(() => new ForwardingLoggerFactory(Mock.Of<ILoggerFactory>(), null))
                .Should().Throw<ArgumentNullException>()
                .WithParameterName("manager");
        }

        [Fact]
        public void AddProvider_ThrowsNotSupportedException()
        {
            TestHelpers.Act(() => _factory.AddProvider(Mock.Of<ILoggerProvider>()))
                .Should().Throw<NotSupportedException>();
        }

        [Fact]
        public void CreateLogger_ReturnsLoggerInstance()
        {
            string categoryName = "TestCategory";
            _mockInnerFactory.Setup(f => f.CreateLogger(categoryName)).Returns(_mockInnerLogger.Object);

            ILogger result = _factory.CreateLogger(categoryName);

            result.Should().NotBeNull();
            result.Should().BeOfType<ForwardingLogger>();
            _mockInnerFactory.Verify(f => f.CreateLogger(categoryName), Times.Once);
        }

        [Fact]
        public void CreateLogger_Caching_ReturnsSameInstance()
        {
            string categoryName = "TestCategory";
            _mockInnerFactory.Setup(f => f.CreateLogger(categoryName)).Returns(_mockInnerLogger.Object);

            ILogger result1 = _factory.CreateLogger(categoryName);
            ILogger result2 = _factory.CreateLogger(categoryName);

            result1.Should().NotBeNull();
            result1.Should().BeOfType<ForwardingLogger>();
            result2.Should().BeSameAs(result1);
            _mockInnerFactory.Verify(f => f.CreateLogger(categoryName), Times.Once);
        }

        [Fact]
        public void Dispose_BlocksCreation()
        {
            _factory.Dispose();
            TestHelpers.Act(() => _factory.CreateLogger("TestCategory"))
                .Should().Throw<ObjectDisposedException>();
        }
    }
}