// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;

namespace WebJobs.Script.Tests
{
    public class CSharpEndToEndTests : EndToEndTestsBase<CSharpEndToEndTests.TestFixture>
    {
        private const string JobLogTestFileName = "joblog.txt";

        public CSharpEndToEndTests(TestFixture fixture) : base(fixture)
        {
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\CSharp")
            {
                File.Delete(JobLogTestFileName);
            }
        }
    }
}
