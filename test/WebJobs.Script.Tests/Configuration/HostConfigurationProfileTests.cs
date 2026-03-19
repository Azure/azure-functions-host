// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class HostConfigurationProfileTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("default")]
        [InlineData("Default")]
        public void Get_Default_ReturnsExpectedProfile(string name)
        {
            HostConfigurationProfile profile = HostConfigurationProfile.Get(name);

            Dictionary<string, string> configDict = new(profile.Configuration);
            profile.Name.Should().Be("default");
            configDict.Should().HaveCount(1);
            configDict.Should().ContainKey("configurationProfile")
                .WhoseValue.Should().Be("default");
        }

        [Theory]
        [InlineData("mcp-custom-handler")]
        [InlineData("MCP-Custom-Handler")]
        [InlineData("web-app-Custom-Handler")]
        [InlineData("WEB-App-Custom-Handler")]
        public void Get_HttpBasedHandler_ReturnsExpectedProfile(string name)
        {
            var expectedProfileName = name.ToLowerInvariant();

            HostConfigurationProfile profile = HostConfigurationProfile.Get(name);
            Dictionary<string, string> configDict = new(profile.Configuration);
            profile.Name.Should().Be(expectedProfileName);
            configDict.Should().HaveCount(5);
            configDict.Should().ContainKey("configurationProfile")
                .WhoseValue.Should().Be(expectedProfileName);
            configDict.Should().ContainKey("customHandler:enableProxyingHttpRequest")
                .WhoseValue.Should().Be("true");
            configDict.Should().ContainKey("extensions:http:routePrefix")
                .WhoseValue.Should().Be(string.Empty);
            configDict.Should().ContainKey("customHandler:http:routes:0:route")
                .WhoseValue.Should().Be("{*route}");
            configDict.Should().ContainKey("ProfileWorkerRuntime")
                .WhoseValue.Should().Be("custom");
        }

        [Fact]
        public void Get_Null_Throws()
        {
            Action action = () => HostConfigurationProfile.Get(null);
            action.Should().ThrowExactly<ArgumentNullException>().WithParameterName("name");
        }

        [Fact]
        public void Get_InvalidName_Throws()
        {
            Action action = () => HostConfigurationProfile.Get("invalid");

            action.Should()
                .ThrowExactly<NotSupportedException>()
                .WithMessage("Configuration profile 'invalid' is not supported. Supported values: '', 'default', 'mcp-custom-handler', 'web-app-custom-handler'.");
        }
    }
}
