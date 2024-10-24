// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class FunctionsHostingConfigOptionsSetupTest
    {
        [Fact]
        public void Configure_Sets_Options()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IConfiguration configuraton = GetConfiguration(fileName, $"feature1=value1,feature2=value2");
                FunctionsHostingConfigOptionsSetup setup = new FunctionsHostingConfigOptionsSetup(configuraton);
                Config.FunctionsHostingConfigOptions options = new Config.FunctionsHostingConfigOptions();
                setup.Configure(options);

                Assert.Equal(options.GetFeature("feature1"), "value1");
            }
        }

        [Fact]
        public void Configure_DoesNotSet_Options_IfFileEmpty()
        {
            using (TempDirectory tempDir = new TempDirectory())
            {
                string fileName = Path.Combine(tempDir.Path, "settings.txt");
                IConfiguration configuraton = GetConfiguration(fileName, string.Empty);
                FunctionsHostingConfigOptionsSetup setup = new FunctionsHostingConfigOptionsSetup(configuraton);
                Config.FunctionsHostingConfigOptions options = new Config.FunctionsHostingConfigOptions();
                setup.Configure(options);

                Assert.Empty(options.Features);
            }
        }

        [Fact]
        public void Configure_DoesNotSet_Options_IfFileDoesNotExist()
        {
            string fileName = Path.Combine("C://settings.txt");
            IConfiguration configuraton = GetConfiguration(fileName, string.Empty);
            FunctionsHostingConfigOptionsSetup setup = new FunctionsHostingConfigOptionsSetup(configuraton);
            FunctionsHostingConfigOptions options = new FunctionsHostingConfigOptions();
            setup.Configure(options);

            Assert.Empty(options.Features);
        }

        private IConfiguration GetConfiguration(string fileName, string fileContent)
        {
            File.WriteAllText(fileName, fileContent);
            Mock<IEnvironment> mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(x => x.GetEnvironmentVariable(It.Is<string>(x => x == EnvironmentSettingNames.FunctionsPlatformConfigFilePath))).Returns(fileName);
            FunctionsHostingConfigSource source = new FunctionsHostingConfigSource(mockEnvironment.Object);
            FunctionsHostingConfigProvider provider = new FunctionsHostingConfigProvider(source);
            return new ConfigurationBuilder().Add(source).Build();
        }
    }
}
