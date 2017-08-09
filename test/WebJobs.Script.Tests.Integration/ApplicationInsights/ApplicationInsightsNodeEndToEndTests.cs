// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public class ApplicationInsightsNodeEndToEndTests : ApplicationInsightsEndToEndTestsBase<ApplicationInsightsNodeEndToEndTests.TestFixture>
    {
        public ApplicationInsightsNodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task ApplicationInsights_Succeeds()
        {
            await ApplicationInsights_SucceedsTest();
        }

        public class TestFixture : ApplicationInsightsTestFixture
        {
            private const string ScriptRoot = @"TestScripts\Node";

            public TestFixture() : base(ScriptRoot, "node")
            {
            }
        }
    }
}
