using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs.Host.TestCommon;

namespace Microsoft.WindowsAzure.Jobs.UnitTestsSdk1
{
    [TestClass]
    public class CloudBlobPathTests 
    {
        [TestMethod]
        public void InvalidContainerName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => new CloudBlobPath(@"container-/blob"), "Invalid container name: container-");
        }

        [TestMethod]
        public void BackslashInBlobName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => new CloudBlobPath(@"container/my\name"), "The given blob name 'my\\name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [TestMethod]
        public void OpenSquareBracketInBlobName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => new CloudBlobPath(@"container/my[name"), "The given blob name 'my[name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [TestMethod]
        public void CloseSquareBracketInBlobName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => new CloudBlobPath(@"container/my]name"), "The given blob name 'my]name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [TestMethod]
        public void HierarchicalBlobName_IsAllowed()
        {
            new CloudBlobPath(@"container/my/blob");
        }
    }
}
