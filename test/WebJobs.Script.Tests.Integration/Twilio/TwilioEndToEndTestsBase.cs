using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Twilio
{
    public class TwilioEndToEndTestsBase : IClassFixture<TwilioEndToEndTestsBase.TestFixture>
    {
        private EndToEndTestFixture _fixture;

        public TwilioEndToEndTestsBase(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task TwilioReference()
        {
            string testData = Guid.NewGuid().ToString();

            // Run function that references Twilio Api
            await _fixture.Host.BeginFunctionAsync("TwilioReference", testData);
            string logs = "";

            await TestHelpers.Await(() =>
            {
                // Wait until input has been processed
                logs = _fixture.Host.GetLog();
                return logs.Contains(testData);
            });

            // Make sure the input string made it all the way through
            Assert.True(logs.Contains(testData));
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture() :
                base(@"TestScripts\CSharp", "csharp", "Microsoft.Azure.WebJobs.Extensions.Twilio", "3.0.0-beta5")
            {
            }

            protected override IEnumerable<string> GetActiveFunctions() => new[] { "TwilioReference" };
        }
    }
}
