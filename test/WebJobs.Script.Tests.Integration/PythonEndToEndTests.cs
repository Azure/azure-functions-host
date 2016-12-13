// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PythonEndToEndTests : EndToEndTestsBase<PythonEndToEndTests.TestFixture>
    {
        public PythonEndToEndTests(TestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task ManualTrigger_Invoke_Succeeds()
        {
            await ManualTrigger_Invoke_SucceedsTest();
        }

        [Fact]
        public async Task QueueTriggerToBlob()
        {
            await QueueTriggerToBlobTest();
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() : base(@"TestScripts\Python", "python")
            {
            }
        }
    }
}
