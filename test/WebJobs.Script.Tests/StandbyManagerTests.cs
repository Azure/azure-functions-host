// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class StandbyManagerTests
    {
        [Fact]
        public async Task Specialize_ResetsConfiguration()
        {
            var mockHostManager = new Mock<IScriptHostManager>();
            mockHostManager.Setup(m => m.State)
                .Returns(ScriptHostState.Running);

            var mockConfiguration = new Mock<IConfigurationRoot>();
            var mockOptionsMonitor = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
            var mockWebHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            var mockEnvironment = new TestEnvironment();

            var manager = new StandbyManager(mockHostManager.Object, mockConfiguration.Object, mockWebHostEnvironment.Object, mockEnvironment, mockOptionsMonitor.Object, null, NullLogger<StandbyManager>.Instance);

            await manager.SpecializeHostAsync();

            mockConfiguration.Verify(c => c.Reload());
        }
    }
}
