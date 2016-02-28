using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
