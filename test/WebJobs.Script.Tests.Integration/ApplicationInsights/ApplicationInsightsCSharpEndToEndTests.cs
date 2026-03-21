// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Xunit;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    [Trait(TestTraits.Group, TestTraits.NonE2EAppInsights)]
    public class ApplicationInsightsCSharpEndToEndTests : ApplicationInsightsEndToEndTestsBase<ApplicationInsightsCSharpEndToEndTests.TestFixture>
    {
        public ApplicationInsightsCSharpEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        public class TestFixture : ApplicationInsightsTestFixture
        {
            private const string ScriptRoot = @"TestScripts\CSharp";

            public TestFixture() : base(ScriptRoot, "dotnet")
            {
            }
        }
    }
}