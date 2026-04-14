// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers.Admin
{
    public class AppCapabilitiesControllerTests
    {
        private readonly Mock<IOptionsMonitor<AppCapabilitiesOptions>> _mockCapabilitiesOptions;
        private readonly Mock<ILogger<AppCapabilitiesController>> _mockLogger;
        private readonly AppCapabilitiesController _controller;
        private readonly AppCapabilitiesOptions _capabilitiesOptions;

        public AppCapabilitiesControllerTests()
        {
            _capabilitiesOptions = new AppCapabilitiesOptions();

            IDictionary<string, string> capabilitiesOptionsDict = (IDictionary<string, string>)_capabilitiesOptions;

            capabilitiesOptionsDict.Add("feature1", "value1");
            capabilitiesOptionsDict.Add("feature2", "value2");
            capabilitiesOptionsDict.Add("extensionSupport", "enabled");

            _mockCapabilitiesOptions = new Mock<IOptionsMonitor<AppCapabilitiesOptions>>(MockBehavior.Strict);
            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(_capabilitiesOptions);

            _mockLogger = new Mock<ILogger<AppCapabilitiesController>>();

            _controller = new AppCapabilitiesController(_mockCapabilitiesOptions.Object, _mockLogger.Object);
        }

        [Fact]
        public void GetCapabilities_ReturnsAllCapabilities()
        {
            var result = _controller.GetCapabilities();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var capabilities = Assert.IsAssignableFrom<IDictionary<string, string>>(okResult.Value);
            Assert.NotNull(capabilities);
            Assert.Equal(3, capabilities.Count);
            Assert.Equal("value1", capabilities["feature1"]);
            Assert.Equal("value2", capabilities["feature2"]);
            Assert.Equal("enabled", capabilities["extensionSupport"]);
        }

        [Fact]
        public void GetCapabilities_ReturnsEmptyDictionary_WhenNoCapabilities()
        {
            var emptyOptions = new AppCapabilitiesOptions();
            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(emptyOptions);

            var result = _controller.GetCapabilities();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var capabilities = Assert.IsAssignableFrom<IDictionary<string, string>>(okResult.Value);
            Assert.NotNull(capabilities);
            Assert.Empty(capabilities);
        }

        [Fact]
        public void GetCapabilities_ReturnsOk_WhenResponseSizeUnderLimit()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = (IDictionary<string, string>)options;

            for (int i = 0; i < 10; i++)
            {
                dict.Add($"capability{i}", $"value{i}");
            }

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(options);

            var result = _controller.GetCapabilities();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var capabilities = Assert.IsAssignableFrom<IDictionary<string, string>>(okResult.Value);
            Assert.NotNull(capabilities);
            Assert.Equal(10, capabilities.Count);

            _mockLogger.Verify(
                x => x.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }

        [Fact]
        public void GetCapabilities_Returns500_WhenValidationFails()
        {
            var validationException = new OptionsValidationException(
                "AppCapabilitiesOptions",
                typeof(AppCapabilitiesOptions),
                new[] { "Capabilities size exceeds maximum allowed size." });

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Throws(validationException);

            var result = _controller.GetCapabilities();

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);

            dynamic errorResponse = statusCodeResult.Value;
            Assert.NotNull(errorResponse.error);
            Assert.Contains("Capabilities size exceeds maximum allowed size.", (string)errorResponse.error);
        }

        [Fact]
        public void GetCapabilities_LogsError_WhenValidationFails()
        {
            var validationException = new OptionsValidationException(
                "AppCapabilitiesOptions",
                typeof(AppCapabilitiesOptions),
                new[] { "Capabilities size exceeds maximum allowed size." });

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Throws(validationException);

            _controller.GetCapabilities();

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Capabilities validation failed")),
                    It.Is<Exception>(ex => ex == validationException),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Theory]
        [InlineData("feature1", "value1")]
        [InlineData("feature2", "value2")]
        [InlineData("extensionSupport", "enabled")]
        public void Get_ReturnsCapability_WhenCapabilityExists(string name, string expectedValue)
        {
            var result = _controller.Get(name);

            var okResult = Assert.IsType<OkObjectResult>(result);
            var value = Assert.IsType<string>(okResult.Value);
            Assert.Equal(expectedValue, value);
        }

        [Theory]
        [InlineData("nonExistentFeature")]
        [InlineData("unknownCapability")]
        [InlineData("")]
        public void Get_ReturnsNotFound_WhenCapabilityDoesNotExist(string name)
        {
            var result = _controller.Get(name);

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            dynamic errorResponse = notFoundResult.Value;
            Assert.NotNull(errorResponse.error);
            Assert.Contains($"Capability '{name}' not found.", (string)errorResponse.error);
        }

        [Fact]
        public void Get_ReturnsNotFound_WhenCapabilitiesIsEmpty()
        {
            var emptyOptions = new AppCapabilitiesOptions();
            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(emptyOptions);

            var result = _controller.Get("anyFeature");

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            dynamic errorResponse = notFoundResult.Value;
            Assert.NotNull(errorResponse.error);
            Assert.Contains("Capability 'anyFeature' not found.", (string)errorResponse.error);
        }

        [Fact]
        public void Get_IsCaseInsensitive()
        {
            var result = _controller.Get("Feature1");
            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public void Get_ReturnsNull_WhenValueIsNull()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = (IDictionary<string, string>)options;

            dict.Add("nullCapability", null);

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(options);

            var result = _controller.Get("nullCapability");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Null(okResult.Value);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }

        [Fact]
        public void Get_Returns500_WhenValidationFails()
        {
            var validationException = new OptionsValidationException(
                "AppCapabilitiesOptions",
                typeof(AppCapabilitiesOptions),
                new[] { "Capabilities size exceeds maximum allowed size." });

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Throws(validationException);

            var result = _controller.Get("largeCapability");

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);

            dynamic errorResponse = statusCodeResult.Value;
            Assert.NotNull(errorResponse.error);
            Assert.Contains("Capabilities size exceeds maximum allowed size.", (string)errorResponse.error);
        }

        [Fact]
        public void Get_LogsError_WhenValidationFails()
        {
            var validationException = new OptionsValidationException(
                "AppCapabilitiesOptions",
                typeof(AppCapabilitiesOptions),
                new[] { "Capabilities size exceeds maximum allowed size." });

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Throws(validationException);

            _controller.Get("largeCapability");

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Capabilities validation failed")),
                    It.Is<Exception>(ex => ex == validationException),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
    }
}
