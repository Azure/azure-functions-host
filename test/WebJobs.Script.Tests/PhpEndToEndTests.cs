// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "PHP-E2E")]
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
