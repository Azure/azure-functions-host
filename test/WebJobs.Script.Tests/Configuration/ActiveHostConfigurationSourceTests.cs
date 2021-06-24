// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ActiveHostConfigurationSourceTests
    {
        private Mock<IScriptHostManager> _mockScriptHostManager;

        public ActiveHostConfigurationSourceTests()
        {
            _mockScriptHostManager = new Mock<IScriptHostManager>();
        }

        [Fact]
        public void RetrievesCorrectValue()
        {
            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AzureWebJobsStorage", "testValue1" },
                { "TestSection1:SomeKey", "value1" },
                { "TestSection1:AnotherKey", "value2" },
                { "ConnectionStrings:ConnStr1", "str1" },
                { "ConnectionStrings:ConnStr2", "str2" },
                { "SectionA:SectionB", "middleValue" },
                { "SectionA:SectionB:SectionC", "finalValue" }
            };

            var testActiveHostConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(testData)
                .Build();

            _mockScriptHostManager.As<IServiceProvider>().Setup(p => p.GetService(typeof(IConfiguration))).Returns(testActiveHostConfig);

            var configurationToTest = new ConfigurationBuilder()
                .Add(new ActiveHostConfigurationSource(_mockScriptHostManager.Object))
                .Build();

            Assert.Equal("str1", configurationToTest.GetWebJobsConnectionStringSection("connStr1").Value);
            Assert.Equal("testValue1", configurationToTest.GetWebJobsConnectionStringSection("Storage").Value);
            Assert.Equal("testValue1", configurationToTest.GetWebJobsConnectionStringSection("AzureWebJobsStorage").Value);
            Assert.True(configurationToTest.GetWebJobsConnectionStringSection("TestSection1").Exists());
            Assert.Equal("value1", configurationToTest.GetWebJobsConnectionStringSection("TestSection1").GetValue<string>("someKey"));

            Assert.True(configurationToTest.GetWebJobsConnectionStringSection("SectionA:SectionB").Exists());
            Assert.Equal("middleValue", configurationToTest.GetWebJobsConnectionStringSection("SectionA:SectionB").Value);
            Assert.Equal("finalValue", configurationToTest.GetWebJobsConnectionStringSection("SectionA:SectionB:SectionC").Value);

            // Test equal behavior to EnvironmentVariableConfigurationProvider
            Assert.Equal(testActiveHostConfig.GetWebJobsConnectionStringSection("connStr1").Value,
                            configurationToTest.GetWebJobsConnectionStringSection("connStr1").Value);
            Assert.Equal(testActiveHostConfig.GetWebJobsConnectionStringSection("Storage").Value,
                            configurationToTest.GetWebJobsConnectionStringSection("Storage").Value);
            Assert.Equal(testActiveHostConfig.GetWebJobsConnectionStringSection("AzureWebJobsStorage").Value,
                            configurationToTest.GetWebJobsConnectionStringSection("AzureWebJobsStorage").Value);
            Assert.Equal(testActiveHostConfig.GetWebJobsConnectionStringSection("TestSection1").Exists(),
                            configurationToTest.GetWebJobsConnectionStringSection("TestSection1").Exists());
            Assert.Equal(testActiveHostConfig.GetWebJobsConnectionStringSection("TestSection1").GetValue<string>("someKey"),
                            configurationToTest.GetWebJobsConnectionStringSection("TestSection1").GetValue<string>("someKey"));
            Assert.Equal(testActiveHostConfig.GetWebJobsConnectionStringSection("SectionA:SectionB:SectionC").Value,
                            configurationToTest.GetWebJobsConnectionStringSection("SectionA:SectionB:SectionC").Value);
        }

        [Fact]
        public void ReloadDataOnUnderlyingConfigurationChange()
        {
            var testData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Key1", "testValue1" }
            };

            using (new TestScopedEnvironmentVariable(testData))
            {
                var testActiveHostConfig = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();

                _mockScriptHostManager.As<IServiceProvider>().Setup(p => p.GetService(typeof(IConfiguration))).Returns(testActiveHostConfig);

                var configurationToTest = new ConfigurationBuilder()
                    .Add(new ActiveHostConfigurationSource(_mockScriptHostManager.Object))
                    .Build();

                Assert.Equal("testValue1", configurationToTest.GetValue<string>("key1"));

                using (new TestScopedEnvironmentVariable("Key2", "testValue2"))
                {
                    testActiveHostConfig.Reload();
                    Assert.Equal("testValue2", testActiveHostConfig.GetValue<string>("key2"));
                    Assert.Equal("testValue2", configurationToTest.GetValue<string>("key2"));
                }
            }
        }

        [Fact]
        public void UnderlyingConfigurationPreservesOrder()
        {
            var firstSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Key1", "valueFromFirstSource" }
            };
            var secondSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Key1", "valueFromSecondSource" }
            };

            var testActiveHostConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(firstSource)
                .AddInMemoryCollection(secondSource)
                .Build();

            _mockScriptHostManager.As<IServiceProvider>().Setup(p => p.GetService(typeof(IConfiguration))).Returns(testActiveHostConfig);

            var configurationToTest = new ConfigurationBuilder()
                .Add(new ActiveHostConfigurationSource(_mockScriptHostManager.Object))
                .Build();

            Assert.Equal("valueFromSecondSource", configurationToTest.GetValue<string>("key1"));
        }
    }
}
