// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.OutOfProc.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class HttpWorkerOptionsSetupTests
    {
        private readonly TestEnvironment _environment = new TestEnvironment();
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();
        private readonly string _hostJsonFile;
        private readonly string _rootPath;
        private readonly ScriptApplicationHostOptions _options;
        private ILoggerProvider _testLoggerProvider;
        private ILoggerFactory _testLoggerFactory;
        private static string _currentDirectory = Directory.GetCurrentDirectory();

        public HttpWorkerOptionsSetupTests()
        {
            _testLoggerProvider = new TestLoggerProvider();
            _testLoggerFactory = new LoggerFactory();
            _testLoggerFactory.AddProvider(_testLoggerProvider);
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
                    'httpInvoker': {
                            'description': {
                                'defaultExecutablePath': 'testExe'
                            }
                        }
                    }")]
        public void MissingOrValid_HttpInvokerConfig_DoesNotThrowException(string hostJsonContent)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var configuration = BuildHostJsonConfiguration();
            HttpWorkerOptionsSetup setup = new HttpWorkerOptionsSetup(configuration, _testLoggerFactory);
            HttpWorkerOptions options = new HttpWorkerOptions();
            var ex = Record.Exception(() => setup.Configure(options));
            Assert.Null(ex);
            if (options.Description != null && !string.IsNullOrEmpty(options.Description.DefaultExecutablePath))
            {
                string expectedDefaultExecutablePath = Path.Combine(Directory.GetCurrentDirectory(), "testExe");
                Assert.Equal(expectedDefaultExecutablePath, options.Description.DefaultExecutablePath);
            }
        }

        [Fact]
        public void InValid_HttpInvokerConfig_Throws_HostConfigurationException()
        {
            string hostJsonContent = @"{
                    'version': '2.0',
                    'httpInvoker': {
                            'invalid': {
                                'defaultExecutablePath': 'testExe'
                            }
                        }
                    }";
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var configuration = BuildHostJsonConfiguration();
            HttpWorkerOptionsSetup setup = new HttpWorkerOptionsSetup(configuration, _testLoggerFactory);
            HttpWorkerOptions options = new HttpWorkerOptions();
            var ex = Assert.Throws<HostConfigurationException>(() => setup.Configure(options));
            Assert.Contains("Missing WorkerDescription for HttpInvoker", ex.Message);
        }

        [Fact]
        public void InValid_HttpInvokerConfig_Throws_ValidationException()
        {
            string hostJsonContent = @"{
                    'version': '2.0',
                    'httpInvoker': {
                            'description': {
                                'langauge': 'testExe'
                            }
                        }
                    }";
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var configuration = BuildHostJsonConfiguration();
            HttpWorkerOptionsSetup setup = new HttpWorkerOptionsSetup(configuration, _testLoggerFactory);
            HttpWorkerOptions options = new HttpWorkerOptions();
            var ex = Assert.Throws<ValidationException>(() => setup.Configure(options));
            Assert.Contains("WorkerDescription DefaultExecutablePath cannot be empty", ex.Message);
        }

        [Theory]
        [InlineData(@"{
                    'version': '2.0',
                    'httpInvoker': {
                            'description': {
                                'arguments': ['--xTest1 --xTest2'],
                                'defaultExecutablePath': 'node',
                                'defaultWorkerPath': 'httpInvoker.js'
                            }
                        }
                    }", false, true, true)]
        [InlineData(@"{
                    'version': '2.0',
                    'httpInvoker': {
                            'description': {
                                'arguments': ['--xTest1 --xTest2'],
                                'defaultExecutablePath': 'node'
                            }
                        }
                    }", true, false, false)]
        [InlineData(@"{
                    'version': '2.0',
                    'httpInvoker': {
                            'description': {
                                'arguments': ['--xTest1 --xTest2'],
                                'defaultExecutablePath': 'c:/myruntime/node'
                            }
                        }
                    }", false, false, false)]
        [InlineData(@"{
                    'version': '2.0',
                    'httpInvoker': {
                            'description': {
                                'arguments': ['--xTest1 --xTest2'],
                                'defaultExecutablePath': 'c:/myruntime/node',
                                'defaultWorkerPath': 'c:/myworkerPath/httpInvoker.js'
                            }
                        }
                    }", false, false, true)]
        public void HttpInvoker_Config_ExpectedValues(string hostJsonContent, bool appendCurrentDirectoryToExe, bool appendCurrentDirToWorkerPath, bool workerPathSet)
        {
            File.WriteAllText(_hostJsonFile, hostJsonContent);
            var configuration = BuildHostJsonConfiguration();
            HttpWorkerOptionsSetup setup = new HttpWorkerOptionsSetup(configuration, _testLoggerFactory);
            HttpWorkerOptions options = new HttpWorkerOptions();
            setup.Configure(options);

            //Verify worker exe path is expected
            if (appendCurrentDirectoryToExe)
            {
                Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "node"), options.Description.DefaultExecutablePath);
            }
            else if (Path.IsPathRooted(options.Description.DefaultExecutablePath))
            {
                Assert.Equal(@"c:/myruntime/node", options.Description.DefaultExecutablePath);
            }
            else
            {
                Assert.Equal("node", options.Description.DefaultExecutablePath);
            }

            //Verify worker path is expected
            if (appendCurrentDirToWorkerPath)
            {
                Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "httpInvoker.js"), options.Description.DefaultWorkerPath);
            }
            else if (!workerPathSet)
            {
                Assert.Null(options.Description.DefaultWorkerPath);
            }
            else
            {
                Assert.Equal(@"c:/myworkerPath/httpInvoker.js", options.Description.DefaultWorkerPath);
            }

            Assert.Equal(1, options.Description.Arguments.Count);
            Assert.Equal("--xTest1 --xTest2", options.Description.Arguments[0]);
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
    }
}
