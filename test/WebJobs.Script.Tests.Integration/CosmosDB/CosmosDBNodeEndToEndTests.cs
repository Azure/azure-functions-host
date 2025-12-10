// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using System.Threading.Tasks;
using Xunit;

// Add this outside the namespace or at the top of the file
[CollectionDefinition("CosmosDBNodeEndToEndTests", DisableParallelization = true)]
public class CosmosDBNodeEndToEndTestsCollection { }

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDB
{
    [Collection("CosmosDBNodeEndToEndTests")]
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

        [Fact]
        public Task TestConnection()
        {
            return TestConnectToEmulator();
        }

        public class TestFixture : CosmosDBEndtoEndTestFixture
        {
            private const string ScriptRoot = @"TestScripts\Node";

            public TestFixture() : base(ScriptRoot, "node", RpcWorkerConstants.NodeLanguageWorkerName)
            {
            }
        }
    }
}
