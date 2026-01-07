// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
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

        [Fact]
        public Task CosmosDB()
        {
            return CosmosDBTest();
        }

        [Fact]
        public Task CosmosDBMultipleItems()
        {
            return CosmosDBMultipleItemsTest();
        }

        public class TestFixture : CosmosDBEndToEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\Node";

            public TestFixture() : base(ScriptRoot, "node", RpcWorkerConstants.NodeLanguageWorkerName)
            {
            }
        }
    }
}
