// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class ScriptLoggerTests
    {
        private TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        [Fact]
        public void ScriptLogger_Scopes()
        {
            // build a minimal logging pipeline.
            IHostBuilder builder = new HostBuilder()
                .ConfigureLogging(b =>
                {
                    b.Services.AddSingleton<ILoggerFactory, ScriptLoggerFactory>();
                    b.Services.AddSingleton<ISystemAssemblyManager, SystemAssemblyManager>();
                    b.Services.Add(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(ScriptLogger<>)));
                    b.AddProvider(_loggerProvider);
                });

            using (IHost host = builder.Build())
            {
                ILogger logger1 = host.Services.GetService<ILogger<ScriptHost>>();
                ILogger logger2 = host.Services.GetService<ILogger<ScriptLoggerTests>>();

                logger1.LogInformation("Test");
                logger2.LogInformation("Test");
            }

            var logs = _loggerProvider.GetAllLogMessages();

            Assert.Equal(2, logs.Count);
            var scope1 = logs.First().Scope;
            var scope2 = logs.Last().Scope;

            var scopeRecord = scope1.Single();
            Assert.Equal(ScriptConstants.LogPropertyIsSystemLogKey, scopeRecord.Key);
            Assert.True((bool)scopeRecord.Value);

            Assert.Empty(scope2);
        }
    }
}
