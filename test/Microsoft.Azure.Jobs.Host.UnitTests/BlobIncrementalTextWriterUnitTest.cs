using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    public class BlobIncrementalTextWriterUnitTest
    {
        [Fact]
        public void TestIncrementalWriter()
        {
            TimeSpan refreshRate = TimeSpan.FromSeconds(1);

            string content = null;
            Action<string> fp = x => { content = x; };
            BlobIncrementalTextWriter writer = new BlobIncrementalTextWriter(fp);

            var tw = writer.Writer;
            tw.Write("1");

            // Ensure content not yet written
            Assert.Equal(null, content);

            writer.Start(refreshRate);

            Assert.Equal("1", content);

            tw.Write("2");
            Thread.Sleep(refreshRate + refreshRate); 

            Assert.Equal("12", content);

            tw.Write("3");
            writer.Close();
            Assert.Equal("123", content);
        }
    }
}
