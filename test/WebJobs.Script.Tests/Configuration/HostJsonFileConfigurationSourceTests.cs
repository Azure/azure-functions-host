// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class HostJsonFileConfigurationSourceTests
    {
        [Fact]
        public void Initialize_Sanitizes_HostJsonLog()
        {
            var loggerFactory = new LoggerFactory();
            TestLoggerProvider loggerProvider = new TestLoggerProvider();
            loggerFactory.AddProvider(loggerProvider);

            string rootPath = Path.Combine(Environment.CurrentDirectory, "ScriptHostTests");
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            // Turn off all logging. We shouldn't see any output.
            string hostJsonContent = @"
            {
                'functionTimeout': '00:05:00',
                'functions': [ 'FunctionA', 'FunctionB' ],
                'logger': {
                    'categoryFilter': {
                        'defaultLevel': 'Information'
                    }
                },
                'Values': {
                    'MyCustomValue': 'abc'
                }
            }";

            File.WriteAllText(Path.Combine(rootPath, "host.json"), hostJsonContent);

            var webHostOptions = new ScriptWebHostOptions
            {
                ScriptPath = rootPath
            };

            var configSource = new HostJsonFileConfigurationSource(new OptionsWrapper<ScriptWebHostOptions>(webHostOptions), loggerFactory);

            var configurationBuilder = new ConfigurationBuilder();
            IConfigurationProvider provider = configSource.Build(configurationBuilder);

            provider.Load();

            string hostJsonSanitized = @"
            {
                'functionTimeout': '00:05:00',
                'functions': [ 'FunctionA', 'FunctionB' ],
                'logger': {
                    'categoryFilter': {
                        'defaultLevel': 'Information'
                    }
                }
            }";

            // for formatting
            var hostJson = JObject.Parse(hostJsonSanitized);

            var logger = loggerProvider.CreatedLoggers.Single(l => l.Category == LogCategories.Startup);
            var logMessage = logger.GetLogMessages().Single(l => l.FormattedMessage.StartsWith("Host configuration file read")).FormattedMessage;
            Assert.Equal($"Host configuration file read:{Environment.NewLine}{hostJson}", logMessage);
        }
    }
}
