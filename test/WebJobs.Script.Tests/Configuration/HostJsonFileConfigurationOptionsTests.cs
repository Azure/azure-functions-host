// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class HostJsonFileConfigurationOptionsTests
    {
        private static readonly Dictionary<string, EnvironmentTest> _environmentTestValues = new()
        {
            ["Empty Environment"] = new(
                new TestEnvironment(),
                new HostJsonFileConfigurationOptions()),
            ["LogicApp=true,WorkerRuntime=something"] = new(
                new TestEnvironment
                {
                    ["APP_KIND"] = "workflowapp",
                    ["FUNCTIONS_WORKER_RUNTIME"] = "something",
                },
                new HostJsonFileConfigurationOptions
                {
                    IsLogicApp = true,
                    WorkerRuntime = "something",
                }),
        };

        public static IEnumerable<object[]> EnvironmentTestData => _environmentTestValues.Keys
            .Select(x => new object[] { x });

        [Fact]
        public void Create_NullEnvironment_Throws()
        {
            Action action = () => HostJsonFileConfigurationOptions.Create(null, new ScriptApplicationHostOptions());

            action.Should().ThrowExactly<ArgumentNullException>().WithParameterName("environment");
        }

        [Fact]
        public void Create_NullScriptOptions_Throws()
        {
            Action action = () => HostJsonFileConfigurationOptions.Create(new TestEnvironment(), null);

            action.Should().ThrowExactly<ArgumentNullException>().WithParameterName("hostOptions");
        }

        [Theory]
        [MemberData(nameof(EnvironmentTestData))]
        public void Create_ValidParameters_ReturnsOptions(string testName)
        {
            EnvironmentTest test = _environmentTestValues[testName];
            HostJsonFileConfigurationOptions options = HostJsonFileConfigurationOptions
                .Create(test.Environment, new());

            options.Should().NotBeNull();
            options.WorkerRuntime.Should().Be(test.Expected.WorkerRuntime);
            options.IsLogicApp.Should().Be(test.Expected.IsLogicApp);
        }

        [Fact]
        public void GetConfigProfile_EnvironmentSet_OverridesHostJson()
        {
            TestEnvironment environment = new()
            {
                ["AzureFunctionsJobHost__configurationProfile"] = "mcp",
            };

            JObject hostFile = JObject.Parse("{ 'configurationProfile': 'default' }");
            HostJsonFileConfigurationOptions options = HostJsonFileConfigurationOptions
                .Create(environment, new());

            HostConfigurationProfile profile = options.GetConfigProfile(hostFile);

            profile.Name.Should().Be("mcp");
        }

        [Fact]
        public void GetConfigProfile_EnvironmentNotSet_UsesHostJson()
        {
            TestEnvironment environment = new();

            JObject hostFile = JObject.Parse("{ 'configurationProfile': 'mcp' }");
            HostJsonFileConfigurationOptions options = HostJsonFileConfigurationOptions
                .Create(environment, new());

            HostConfigurationProfile profile = options.GetConfigProfile(hostFile);

            profile.Name.Should().Be("mcp");
        }

        private record EnvironmentTest(
            IEnvironment Environment, HostJsonFileConfigurationOptions Expected);
    }
}
