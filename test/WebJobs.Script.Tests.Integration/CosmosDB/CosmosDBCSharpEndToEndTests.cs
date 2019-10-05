// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDB
{
    public class CosmosDBCSharpEndToEndTests :
        CosmosDBEndToEndTestsBase<CosmosDBCSharpEndToEndTests.TestFixture>
    {
        public CosmosDBCSharpEndToEndTests(TestFixture fixture) : base(fixture)
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

        public class TestFixture : CosmosDBTestFixture
        {
            private const string ScriptRoot = @"TestScripts\CSharp";

            public TestFixture() : base(ScriptRoot, "csharp", RpcWorkerConstants.DotNetLanguageWorkerName)
            {
            }
        }
    }
}
