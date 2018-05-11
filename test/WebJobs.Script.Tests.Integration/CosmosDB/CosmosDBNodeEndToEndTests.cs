// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDB
{
    public class CosmosDBNodeEndToEndTests :
        CosmosDBEndToEndTestsBase<CosmosDBNodeEndToEndTests.TestFixture>
    {
        public CosmosDBNodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public Task CosmosDBTrigger()
        {
            return CosmosDBTriggerToBlobTest();
        }

        [Fact(Skip = "Frequent failures when running in CI. Passes locally needs to be investigated https://github.com/Azure/azure-functions-host/issues/2837")]
        public Task CosmosDB()
        {
            return CosmosDBTest();
        }

        public class TestFixture : CosmosDBTestFixture
        {
            private const string ScriptRoot = @"TestScripts\Node";

            public TestFixture() : base(ScriptRoot, "node")
            {
            }
        }
    }
}
