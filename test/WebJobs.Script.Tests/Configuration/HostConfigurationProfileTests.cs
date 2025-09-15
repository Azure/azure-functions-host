// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using FluentAssertions;
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

            profile.Name.Should().Be("default");
            profile.Configuration.Should().BeEmpty();
        }

        [Theory]
        [InlineData("mcp")]
        [InlineData("MCP")]
        public void Get_Mcp_ReturnsExpectedProfile(string name)
        {
            HostConfigurationProfile profile = HostConfigurationProfile.Get(name);

            profile.Name.Should().Be("mcp");
            profile.Configuration.Should().HaveCount(2);
            profile.Configuration["customHandler:enableHttpProxyingRequest"].Should().Be("true");
            profile.Configuration["extensions:http:routePrefix"].Should().Be(string.Empty);
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
                .ThrowExactly<ArgumentException>()
                .WithMessage("Configuration profile 'invalid' is not supported. Supported values: '', 'default', 'mcp'.");
        }
    }
}
