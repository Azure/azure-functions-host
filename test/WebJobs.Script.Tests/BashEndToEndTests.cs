// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WebJobs.Script.Tests
{
    public class BashEndToEndTests : EndToEndTestsBase<BashEndToEndTests.TestFixture>
    {
        public BashEndToEndTests(TestFixture fixture)
            : base(fixture)
        {
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Bash")
            {
            }
        }
    }
}
