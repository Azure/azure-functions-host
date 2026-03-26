// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.AppCapabilities
{
    public class ScriptHostRestartCapabilitiesTests
    {
        [Fact]
        public async Task ScriptHostRestart_ClearsCapabilities()
        {
            using var testHost = new TestFunctionHost(@"TestScripts\Empty");

            var capabilitiesStore = testHost.JobHostServices.GetRequiredService<IAppCapabilitiesStore>();

            // Set initial capabilities
            var capabilities = new Dictionary<string, string>
            {
                { "feature1", "value1" },
                { "workerRuntime", "dotnet" }
            };
            capabilitiesStore.TrySetAll(capabilities);

            Assert.Equal(2, capabilitiesStore.Capabilities.Count);

            // Trigger restart
            await testHost.RestartAsync(CancellationToken.None);

            // Wait for host to be running again
            await TestHelpers.Await(() => testHost.JobHostServices is not null, userMessageCallback: testHost.GetLog);

            // Verify capabilities were cleared
            var capabilitiesStoreAfterRestart = testHost.JobHostServices.GetRequiredService<IAppCapabilitiesStore>();
            Assert.Throws<InvalidOperationException>(() => { var _ = capabilitiesStoreAfterRestart.Capabilities; });
        }
    }
}
