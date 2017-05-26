// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Loggers
{
    public class UpdateOutputLogCommandTests
    {
        [Fact]
        public void TestIncrementalWriter()
        {
            string content = null;
            Func<string, CancellationToken, Task> fp = (x, _) =>
            {
                content = x;
                return Task.FromResult(0);
            };

            UpdateOutputLogCommand writer = UpdateOutputLogCommand.Create(new Mock<IStorageBlockBlob>().Object, fp);

            var tw = writer.Output;
            tw.Write("1");

            // Ensure content not yet written
            Assert.Equal(null, content);

            writer.TryExecute();

            Assert.Equal("1", content);

            tw.Write("2");
            writer.TryExecute();

            Assert.Equal("12", content);

            tw.Write("3");
            writer.SaveAndClose();
            Assert.Equal("123", content);
        }

        [Fact]
        public async Task TestMultipleThreads()
        {
            // This validates a bug where flushing from one thread while writing from another
            // would cause an exception. 

            string content = null;
            Func<string, CancellationToken, Task> fp = (x, _) => { content = x; return Task.FromResult(0); };
            UpdateOutputLogCommand writer = UpdateOutputLogCommand.Create(new Mock<IStorageBlockBlob>().Object, fp);

            var tw = writer.Output;
            bool writeDone = false;

            // Start a Task to flush
            Task flushTask = Task.Run(() =>
            {
                while (!writeDone)
                {
                    writer.TryExecute();
                }
            });

            // Start a Task to write
            Task writeTask = Task.Run(() =>
            {
                for (int i = 0; i < 10000000; i++)
                {
                    tw.WriteLine(string.Empty);
                }
                writeDone = true;
            });

            await flushTask;
            await writeTask;
        }
    }
}
