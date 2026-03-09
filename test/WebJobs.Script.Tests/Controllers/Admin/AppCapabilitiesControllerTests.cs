// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
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
            var capabilities = (IDictionary<string, string>)okResult.Value;
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
            var capabilities = (IDictionary<string, string>)okResult.Value;
            Assert.Empty(capabilities);
        }

        [Fact]
        public void GetCapabilities_DoesNotTrim_WhenResponseSizeUnderLimit()
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
            var capabilities = (IDictionary<string, string>)okResult.Value;
            Assert.Equal(10, capabilities.Count);

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
        public void GetCapabilities_TrimsResponse_WhenResponseSizeExceedsLimit()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = (IDictionary<string, string>)options;

            var largeValue = new string('x', ScriptConstants.MaxTriggersStringLength / 2);
            dict.Add("capability1", largeValue);
            dict.Add("capability2", largeValue);
            dict.Add("capability3", largeValue);

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(options);

            var result = _controller.GetCapabilities();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var capabilities = (IDictionary<string, string>)okResult.Value;

            Assert.True(capabilities.Count < 3);

            var serialized = JsonSerializer.Serialize(capabilities);
            var responseSize = Encoding.UTF8.GetByteCount(serialized);
            Assert.True(responseSize <= ScriptConstants.MaxTriggersStringLength);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("exceeds maximum allowed size")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public void GetCapabilities_LogsWarning_WhenTrimmingOccurs()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = (IDictionary<string, string>)options;

            var largeValue = new string('y', ScriptConstants.MaxTriggersStringLength);
            dict.Add("largeCapability", largeValue);

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(options);

            _controller.GetCapabilities();

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Response trimmed from")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public void GetCapabilities_TrimsInAlphabeticalOrder()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = (IDictionary<string, string>)options;

            var largeValue = new string('z', ScriptConstants.MaxTriggersStringLength / 3);
            dict.Add("zCapability", largeValue);
            dict.Add("aCapability", largeValue);
            dict.Add("mCapability", largeValue);

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(options);

            var result = _controller.GetCapabilities();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var capabilities = (IDictionary<string, string>)okResult.Value;

            if (capabilities.Count < 3)
            {
                Assert.True(capabilities.ContainsKey("aCapability"));
            }
        }

        [Theory]
        [InlineData("feature1", "value1")]
        [InlineData("feature2", "value2")]
        [InlineData("extensionSupport", "enabled")]
        public void Get_ReturnsCapability_WhenCapabilityExists(string name, string expectedValue)
        {
            var result = _controller.Get(name);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expectedValue, okResult.Value);
        }

        [Theory]
        [InlineData("nonExistentFeature")]
        [InlineData("unknownCapability")]
        [InlineData("")]
        public void Get_ReturnsNotFound_WhenCapabilityDoesNotExist(string name)
        {
            var result = _controller.Get(name);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public void Get_ReturnsNotFound_WhenCapabilitiesIsEmpty()
        {
            var emptyOptions = new AppCapabilitiesOptions();
            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(emptyOptions);

            var result = _controller.Get("anyFeature");

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public void Get_IsCaseInsensitive()
        {
            var result = _controller.Get("Feature1");

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public void Get_DoesNotTrim_WhenValueSizeUnderLimit()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = (IDictionary<string, string>)options;

            var normalValue = "normalSizedValue";
            dict.Add("testCapability", normalValue);

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(options);

            var result = _controller.Get("testCapability");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(normalValue, okResult.Value);

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
        public void Get_TrimsValue_WhenValueSizeExceedsLimit()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = (IDictionary<string, string>)options;

            var largeValue = new string('a', ScriptConstants.MaxTriggersStringLength + 1000);
            dict.Add("largeCapability", largeValue);

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(options);

            var result = _controller.Get("largeCapability");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedValue = (string)okResult.Value;

            Assert.NotEqual(largeValue, returnedValue);
            Assert.True(returnedValue.Length < largeValue.Length);

            var valueSize = Encoding.UTF8.GetByteCount(returnedValue);
            Assert.True(valueSize <= ScriptConstants.MaxTriggersStringLength);

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("exceeds maximum allowed size")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public void Get_HandlesMultiByteCharacters_WhenTrimming()
        {
            var options = new AppCapabilitiesOptions();
            IDictionary<string, string> dict = (IDictionary<string, string>)options;

            var multiByteValue = new string('日', ScriptConstants.MaxTriggersStringLength / 2);
            dict.Add("multiByteCapability", multiByteValue);

            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(options);

            var result = _controller.Get("multiByteCapability");

            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedValue = (string)okResult.Value;

            var valueSize = Encoding.UTF8.GetByteCount(returnedValue);
            Assert.True(valueSize <= ScriptConstants.MaxTriggersStringLength);
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
    }
}