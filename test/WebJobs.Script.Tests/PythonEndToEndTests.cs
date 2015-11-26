// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WebJobs.Script.Tests
{
    public class PythonEndToEndTests : EndToEndTestsBase<PythonEndToEndTests.TestFixture>
    {
        public PythonEndToEndTests(TestFixture fixture)
            : base(fixture)
        {
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Python")
            {
            }
        }
    }
}
