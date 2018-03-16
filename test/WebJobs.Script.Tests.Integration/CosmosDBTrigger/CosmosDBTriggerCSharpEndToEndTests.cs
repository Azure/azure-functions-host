// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDBTrigger
{
    public class CosmosDBTriggerCSharpEndToEndTests :
        CosmosDBTriggerEndToEndTestsBase<CosmosDBTriggerCSharpEndToEndTests.TestFixture>
    {
        public CosmosDBTriggerCSharpEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public Task CosmosDBTrigger()
        {
            return CosmosDBTriggerToBlobTest();
        }

        public class TestFixture : CosmosDBTriggerTestFixture
        {
            private const string ScriptRoot = @"TestScripts\CSharp";

            public TestFixture() : base(ScriptRoot, "csharp")
            {
            }
        }
    }
}
