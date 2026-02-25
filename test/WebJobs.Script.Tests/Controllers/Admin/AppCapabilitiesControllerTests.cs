// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
            _capabilitiesOptions.Capabilities.Add("feature1", "value1");
            _capabilitiesOptions.Capabilities.Add("feature2", "value2");
            _capabilitiesOptions.Capabilities.Add("extensionSupport", "enabled");

            _mockCapabilitiesOptions = new Mock<IOptionsMonitor<AppCapabilitiesOptions>>(MockBehavior.Strict);
            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(_capabilitiesOptions);

            _mockLogger = new Mock<ILogger<AppCapabilitiesController>>();

            _controller = new AppCapabilitiesController(_mockCapabilitiesOptions.Object);
        }

        [Fact]
        public void GetCapabilities_ReturnsAllCapabilities()
        {
            var result = _controller.GetCapabilities();

            var okResult = Assert.IsType<OkObjectResult>(result);
            var capabilities = Assert.IsType<Dictionary<string, string>>(okResult.Value);
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
            var capabilities = Assert.IsType<Dictionary<string, string>>(okResult.Value);
            Assert.Empty(capabilities);
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
        public void Get_HandlesNullValue()
        {
            var optionsWithNull = new AppCapabilitiesOptions();
            optionsWithNull.Capabilities.Add("nullFeature", null);
            _mockCapabilitiesOptions.Setup(o => o.CurrentValue).Returns(optionsWithNull);

            var result = _controller.Get("nullFeature");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Null(okResult.Value);
        }
    }
}