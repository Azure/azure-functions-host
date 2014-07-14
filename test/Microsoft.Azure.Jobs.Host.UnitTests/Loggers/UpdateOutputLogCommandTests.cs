using System;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Loggers
{
    public class UpdateOutputLogCommandTests
    {
        [Fact]
        public void TestIncrementalWriter()
        {
            string content = null;
            Action<string> fp = x => { content = x; };
            UpdateOutputLogCommand writer = new UpdateOutputLogCommand(new CloudBlockBlob(new Uri("aa://b/c")), null, fp);

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
    }
}
