// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebJobsScriptHostServiceTests
    {
        [Fact]
        public async Task HostInitialization_OnInitializationException_MaintainsErrorInformation()
        {
            var options = new ScriptApplicationHostOptions
            {
                ScriptPath = @"c:\tests",
                LogPath = @"c:\tests\logs",
            };

            var monitor = new ScriptApplicationHostOptionsMonitor(options);

            var services = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider();

            var host = new Mock<IHost>();
            host.Setup(h => h.Services)
                .Returns(services);
            host.SetupSequence(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Throws(new HostInitializationException("boom"))
                .Returns(Task.CompletedTask);

            var hostBuilder = new Mock<IScriptHostBuilder>();
            hostBuilder.Setup(b => b.BuildHost(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(host.Object);

            var hostService = new WebJobsScriptHostService(monitor, hostBuilder.Object, NullLoggerFactory.Instance);

            await hostService.StartAsync(CancellationToken.None);

            Assert.Equal(ScriptHostState.Error, hostService.State);
            Assert.IsType<HostInitializationException>(hostService.LastError);
        }
    }
}
