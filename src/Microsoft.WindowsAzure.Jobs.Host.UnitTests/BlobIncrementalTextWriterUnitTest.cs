using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.WindowsAzure.Jobs.Host.UnitTests
{
    [TestClass]
    public class BlobIncrementalTextWriterUnitTest
    {
        [TestMethod]
        public void TestIncrementalWriter()
        {
            TimeSpan refreshRate = TimeSpan.FromSeconds(1);

            string content = null;
            Action<string> fp = x => { content = x; };
            BlobIncrementalTextWriter writer = new BlobIncrementalTextWriter(fp);

            var tw = writer.Writer;
            tw.Write("1");

            Assert.AreEqual(null, content, "Not yet written");

            writer.Start(refreshRate);

            Assert.AreEqual("1", content);

            tw.Write("2");
            Thread.Sleep(refreshRate + refreshRate); 

            Assert.AreEqual("12", content);

            tw.Write("3");
            writer.Close();
            Assert.AreEqual("123", content);
        }
    }
}
