// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    public abstract class EndToEndTestsBase<TTestFixture> : 
        IClassFixture<TTestFixture> where TTestFixture : EndToEndTestFixture, new()
    {
        public EndToEndTestsBase(TTestFixture fixture)
        {
            Fixture = fixture;
        }

        protected TTestFixture Fixture { get; private set; }

        [Fact]
        public async Task QueueTriggerToBlobTest()
        {
            string id = Guid.NewGuid().ToString();
            string messageContent = string.Format("{{ \"id\": \"{0}\" }}", id);
            CloudQueueMessage message = new CloudQueueMessage(messageContent);

            await Fixture.TestQueue.AddMessageAsync(message);

            var resultBlob = Fixture.TestContainer.GetBlockBlobReference(id);
            string result = await TestHelpers.WaitForBlobAsync(resultBlob);
            Assert.Equal(TestHelpers.RemoveByteOrderMarkAndWhitespace(messageContent), TestHelpers.RemoveByteOrderMarkAndWhitespace(result));

            TraceEvent scriptTrace = Fixture.TraceWriter.Traces.SingleOrDefault(p => p.Message.Contains(id));
            Assert.Equal(TraceLevel.Verbose, scriptTrace.Level);

            string trace = TestHelpers.RemoveByteOrderMarkAndWhitespace(scriptTrace.Message);
            Assert.True(trace.Contains(TestHelpers.RemoveByteOrderMarkAndWhitespace("script processed queue message")));
            Assert.True(trace.Contains(TestHelpers.RemoveByteOrderMarkAndWhitespace(messageContent)));
        }
    }
}