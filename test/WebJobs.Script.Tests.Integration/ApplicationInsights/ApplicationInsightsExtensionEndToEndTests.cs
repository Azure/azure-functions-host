// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;
using Xunit;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    [Trait(TestTraits.Group, TestTraits.NonE2EAppInsights)]
    public class ApplicationInsightsExtensionEndToEndTests : ApplicationInsightsEndToEndTestsBase<ApplicationInsightsExtensionEndToEndTests.TestFixture>
    {
        public ApplicationInsightsExtensionEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        public class TestFixture : ApplicationInsightsTestFixture
        {
            public TestFixture() : base(@"TestScripts\NoFunction", null,
                functionProvider: new TestCodelessFunctionProvider(
                [
                    HostInfrastructureTestFunctions.Manual(),
                    HostInfrastructureTestFunctions.Http("HttpTrigger-Scenarios", nameof(HostInfrastructureTestFunctions.HttpScenarios))
                ], null))
            {
            }
        }
    }
}