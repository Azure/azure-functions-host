// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ExtensionBundleConfigHelperTest
    {
        private readonly TestEnvironment _environment = new TestEnvironment();
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();
        private readonly string _hostJsonFile;
        private readonly string _rootPath;
        private readonly ScriptApplicationHostOptions _options;

        public ExtensionBundleConfigHelperTest()
        {
            _rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests");
            Environment.SetEnvironmentVariable(AzureWebJobsScriptRoot, _rootPath);

            if (!Directory.Exists(_rootPath))
            {
                Directory.CreateDirectory(_rootPath);
            }

            _options = new ScriptApplicationHostOptions
            {
                ScriptPath = _rootPath
            };

            _hostJsonFile = Path.Combine(_rootPath, "host.json");
            if (File.Exists(_hostJsonFile))
            {
                File.Delete(_hostJsonFile);
            }
        }

        [Theory]
        [InlineData(@"{
                    'version': '2.0',
                    }")]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': 'Microsoft.Azure.Functions.ExtensionBundle',
                        'version': '[4.*, 5.0.0)'
                        }
                    }")]
        public void MissingOrValidExtenstionConfig_DoesNotThrowException(string hostJsonContent)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var configuration = BuildHostJsonConfiguration();

            ExtensionBundleConfigurationHelper setup = new ExtensionBundleConfigurationHelper(configuration, _environment);
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            var ex = Record.Exception(() => setup.Configure(options));
            Assert.Null(ex);
        }

        [Fact]
        public void ValidExtenstionConfig_ForAppServiceEnvironment_ConfiguresDownloadPath()
        {
            string hostJsonContent = @"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': 'Microsoft.Azure.Functions.ExtensionBundle',
                        'version': '[4.*, 5.0.0)'
                        }
                    }";
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var hostingEnvironment = new Mock<IHostingEnvironment>();

            var configuration = BuildHostJsonConfiguration();
            ExtensionBundleConfigurationHelper setup = new ExtensionBundleConfigurationHelper(configuration, CreateTestAppServiceEnvironment());
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            var ex = Record.Exception(() => setup.Configure(options));

            string expectedDownloadPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                    ? @"D:\home\data\Functions\ExtensionBundles\Microsoft.Azure.Functions.ExtensionBundle"
                                    : "/home/data/Functions/ExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle";
            Assert.Equal(expectedDownloadPath, options.DownloadPath);
            Assert.Null(ex);
        }

        [Fact]
        public void ValidExtenstionConfig_ForAppServiceEnvironment_ConfiguresProbingPaths()
        {
            string hostJsonContent = @"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': 'Microsoft.Azure.Functions.ExtensionBundle',
                        'version': '[4.*, 5.0.0)'
                        }
                    }";
            File.WriteAllText(_hostJsonFile, hostJsonContent);

            var configuration = BuildHostJsonConfiguration();
            ExtensionBundleConfigurationHelper setup = new ExtensionBundleConfigurationHelper(configuration, CreateTestAppServiceEnvironment());
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            setup.Configure(options);

            List<string> expectedProbingPaths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                    ? new List<string>
                                    {
                                        @"C:\Program Files (x86)\FuncExtensionBundles\Microsoft.Azure.Functions.ExtensionBundle"
                                    }
                                    : new List<string>
                                    {
                                        "/FuncExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle",
                                        "/home/site/wwwroot/.azureFunctions/ExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle"
                                    };

            Assert.Equal(expectedProbingPaths, options.ProbingPaths);
        }

        [Fact]
        public void ValidExtenstionConfig_NonAppServiceEnvironment_DoesNotConfigureDownloadPathAndProbingPath()
        {
            string hostJsonContent = @"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': 'Microsoft.Azure.Functions.ExtensionBundle',
                        'version': '[4.*, 5.0.0)'
                        }
                    }";
            File.WriteAllText(_hostJsonFile, hostJsonContent);

            var configuration = BuildHostJsonConfiguration();
            ExtensionBundleConfigurationHelper setup = new ExtensionBundleConfigurationHelper(configuration, _environment);
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            setup.Configure(options);

            Assert.Equal(options.Id, "Microsoft.Azure.Functions.ExtensionBundle");
            Assert.Equal(options.Version.OriginalString, "[4.*, 5.0.0)");
            Assert.True(string.IsNullOrEmpty(options.DownloadPath));
            Assert.Equal(options.ProbingPaths.Count, 0);
        }

        [Theory]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': null
                    }")]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': 'Invalid'
                    }")]
        public void ExtenstionConfig_SetToAnInvalidValue_Throws(string hostJsonContent)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);

            var configuration = BuildHostJsonConfiguration();

            ExtensionBundleConfigurationHelper setup = new ExtensionBundleConfigurationHelper(configuration, _environment);
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            var ex = Assert.Throws<ArgumentException>(() => setup.Configure(options));
            Assert.StartsWith($"The value of version property in extensionBundle section of {ScriptConstants.HostMetadataFileName} file is invalid or missing.", ex.Message);
        }

        [Theory]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': null,
                        'version': '1.0.0'
                        }
                    }")]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': 'Microsoft,,',
                        'version': '1.0.0'
                        }
                    }")]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': {
                        'version': '1.0.0'
                        }
                    }")]
        public void ValidateBundleId_InvalidId_Throws(string hostJsonContent)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var configuration = BuildHostJsonConfiguration();

            ExtensionBundleConfigurationHelper setup = new ExtensionBundleConfigurationHelper(configuration, _environment);
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            var ex = Assert.Throws<ArgumentException>(() => setup.Configure(options));
            Assert.StartsWith($"The value of id property in extensionBundle section of {ScriptConstants.HostMetadataFileName} file is invalid or missing.", ex.Message);
        }

        [Theory]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': 'Microsoft.Azure.Functions.ExtensionBundle',
                        'version': null
                        }
                    }")]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': 'Microsoft.Azure.Functions.ExtensionBundle',
                        'version': '(1.0.0'
                        }
                    }")]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': 'Microsoft.Azure.Functions.ExtensionBundle',
                        'version': 'random'
                        }
                    }")]
        [InlineData(@"{
                    'version': '2.0',
                    'extensionBundle': {
                        'id': 'Microsoft.Azure.Functions.ExtensionBundle'
                        }
                    }")]
        public void ExtensionBundleConfigure_InvalidVersion_ThrowsException(string hostJsonContent)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var configuration = BuildHostJsonConfiguration();

            ExtensionBundleConfigurationHelper setup = new ExtensionBundleConfigurationHelper(configuration, _environment);
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            var ex = Assert.Throws<ArgumentException>(() => setup.Configure(options));
            Assert.StartsWith($"The value of version property in extensionBundle section of {ScriptConstants.HostMetadataFileName} file is invalid or missing.", ex.Message);
        }

        private IConfiguration BuildHostJsonConfiguration(IEnvironment environment = null)
        {
            environment = environment ?? new TestEnvironment();

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            var configSource = new HostJsonFileConfigurationSource(_options, environment, loggerFactory, new TestMetricsLogger());

            var configurationBuilder = new ConfigurationBuilder()
                .Add(configSource);

            return configurationBuilder.Build();
        }

        private TestEnvironment CreateTestAppServiceEnvironment()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(AzureWebsiteInstanceId, Guid.NewGuid().ToString("N"));

            string downloadPath = string.Empty;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                environment.SetEnvironmentVariable(AzureWebsiteHomePath, "D:\\home");
            }
            else
            {
                environment.SetEnvironmentVariable(AzureWebsiteHomePath, "//home");
            }
            return environment;
        }
    }
}