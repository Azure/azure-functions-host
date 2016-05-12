// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PhpEndToEndTests : EndToEndTestsBase<PhpEndToEndTests.TestFixture>
    {
        public PhpEndToEndTests(TestFixture fixture)
            : base(fixture)
        {
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Php", "php")
            {
            }
        }
    }
}
