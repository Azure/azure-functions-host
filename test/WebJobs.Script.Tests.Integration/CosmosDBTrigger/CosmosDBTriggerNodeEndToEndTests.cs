﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Tests.CosmosDBTrigger
{
    public class CosmosDBTriggerNodeEndToEndTests :
        CosmosDBTriggerEndToEndTestsBase<CosmosDBTriggerNodeEndToEndTests.TestFixture>
    {
        public CosmosDBTriggerNodeEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        public class TestFixture : CosmosDBTriggerTestFixture
        {
            private const string ScriptRoot = @"TestScripts\Node";

            public TestFixture() : base(ScriptRoot, "node")
            {
            }
        }
    }
}
