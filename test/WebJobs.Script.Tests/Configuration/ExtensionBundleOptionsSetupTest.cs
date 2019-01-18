// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.BindingExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ExtensionBundleOptionsSetupTest
    {
        private readonly TestEnvironment _environment = new TestEnvironment();
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();
        private readonly string _hostJsonFile;
        private readonly ScriptApplicationHostOptions _options;

        public ExtensionBundleOptionsSetupTest()
        {
            string rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests");

            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            _options = new ScriptApplicationHostOptions
            {
                ScriptPath = rootPath
            };

            _hostJsonFile = Path.Combine(rootPath, "host.json");
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
                        'version': '(1.0.0,2.0.0]'
                        }
                    }")]
        public void MissingOrValidExtenstionConfig_DoesNotThrowException(string hostJsonContent)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            Assert.True(File.Exists(_hostJsonFile));
            var configuration = BuildHostJsonConfiguration();

            ExtensionBundleOptionsSetup setup = new ExtensionBundleOptionsSetup(configuration);
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            var ex = Record.Exception(() => setup.Configure(options));
            Assert.Null(ex);
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
            Assert.True(File.Exists(_hostJsonFile));
            var configuration = BuildHostJsonConfiguration();

            ExtensionBundleOptionsSetup setup = new ExtensionBundleOptionsSetup(configuration);
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            var ex = Assert.Throws<ArgumentException>(() => setup.Configure(options));
            Assert.StartsWith($"The value of id property in extensionBundle section of {ScriptConstants.HostMetadataFileName} file is invalid or missing.", ex.Message);
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
            Assert.True(File.Exists(_hostJsonFile));
            var configuration = BuildHostJsonConfiguration();

            ExtensionBundleOptionsSetup setup = new ExtensionBundleOptionsSetup(configuration);
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
            Assert.True(File.Exists(_hostJsonFile));
            var configuration = BuildHostJsonConfiguration();

            ExtensionBundleOptionsSetup setup = new ExtensionBundleOptionsSetup(configuration);
            ExtensionBundleOptions options = new ExtensionBundleOptions();
            var ex = Assert.Throws<ArgumentException>(() => setup.Configure(options));
            Assert.StartsWith($"The value of version property in extensionBundle section of {ScriptConstants.HostMetadataFileName} file is invalid or missing.", ex.Message);
        }

        private IConfiguration BuildHostJsonConfiguration(IEnvironment environment = null)
        {
            environment = environment ?? new TestEnvironment();

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            var configSource = new HostJsonFileConfigurationSource(_options, environment, loggerFactory);

            var configurationBuilder = new ConfigurationBuilder()
                .Add(configSource);

            return configurationBuilder.Build();
        }
    }
}
