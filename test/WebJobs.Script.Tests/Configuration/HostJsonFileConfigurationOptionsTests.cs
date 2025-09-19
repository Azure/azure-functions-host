// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
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
                new HostJsonFileConfigurationOptions(new())),
            ["LogicApp=true,WorkerRuntime=something"] = new(
                new TestEnvironment
                {
                    ["APP_KIND"] = "workflowapp",
                    ["FUNCTIONS_WORKER_RUNTIME"] = "something",
                },
                new HostJsonFileConfigurationOptions(new())
                {
                    IsLogicApp = true,
                    WorkerRuntime = "something",
                }),
        };

        public static IEnumerable<object[]> EnvironmentTestData => _environmentTestValues.Keys
            .Select(x => new object[] { x });

        [Fact]
        public void Ctor_NullEnvironment_Throws()
        {
            Action action = () => new HostJsonFileConfigurationOptions(null, new ScriptApplicationHostOptions());
            action.Should().ThrowExactly<ArgumentNullException>().WithParameterName("environment");
        }

        [Fact]
        public void Ctor_NullScriptOptions_Throws()
        {
            Action action = () => new HostJsonFileConfigurationOptions(new TestEnvironment(), null);
            action.Should().ThrowExactly<ArgumentNullException>().WithParameterName("hostOptions");
        }

        [Theory]
        [MemberData(nameof(EnvironmentTestData))]
        public void Ctor_ValidParameters_ReturnsOptions(string testName)
        {
            EnvironmentTest test = _environmentTestValues[testName];
            HostJsonFileConfigurationOptions options = new(test.Environment, new());

            options.Should().NotBeNull();
            options.WorkerRuntime.Should().Be(test.Expected.WorkerRuntime);
            options.IsLogicApp.Should().Be(test.Expected.IsLogicApp);
        }

        [Fact]
        public void GetConfigProfile_EnvironmentSet_OverridesHostJson()
        {
            TestEnvironment environment = new()
            {
                ["AzureFunctionsJobHost__configurationProfile"] = "mcp-custom-handler",
            };

            JObject hostFile = JObject.Parse("{ 'configurationProfile': 'default' }");
            HostJsonFileConfigurationOptions options = new(environment, new());

            HostConfigurationProfile profile = options.GetConfigProfile(hostFile);

            profile.Name.Should().Be("mcp-custom-handler");
        }

        [Fact]
        public void GetConfigProfile_EnvironmentNotSet_UsesHostJson()
        {
            TestEnvironment environment = new();

            JObject hostFile = JObject.Parse("{ 'configurationProfile': 'mcp-custom-handler' }");
            HostJsonFileConfigurationOptions options = new(environment, new());

            HostConfigurationProfile profile = options.GetConfigProfile(hostFile);

            profile.Name.Should().Be("mcp-custom-handler");
        }

        private record EnvironmentTest(
            IEnvironment Environment, HostJsonFileConfigurationOptions Expected);
    }
}
