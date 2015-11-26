// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WebJobs.Script.Tests
{
    public class WindowsBatchEndToEndTests : EndToEndTestsBase<WindowsBatchEndToEndTests.TestFixture>
    {
        public WindowsBatchEndToEndTests(TestFixture fixture) 
            : base(fixture)
        {
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\WindowsBatch")
            {
            }
        }
    }
}
