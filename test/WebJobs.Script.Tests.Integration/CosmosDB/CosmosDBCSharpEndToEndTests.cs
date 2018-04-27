// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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

        [Fact(Skip = "https://github.com/Azure/azure-functions-host/issues/2760")]
        public Task CosmosDBTrigger()
        {
            return CosmosDBTriggerToBlobTest();
        }

        [Fact(Skip = "https://github.com/Azure/azure-functions-host/issues/2760")]
        public Task CosmosDB()
        {
            return CosmosDBTest();
        }

        public class TestFixture : CosmosDBTestFixture
        {
            private const string ScriptRoot = @"TestScripts\CSharp";

            public TestFixture() : base(ScriptRoot, "csharp")
            {
            }
        }
    }
}
