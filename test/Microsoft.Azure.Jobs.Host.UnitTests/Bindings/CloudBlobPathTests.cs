using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Bindings
{
    public class CloudBlobPathTests 
    {
        [Fact]
        public void InvalidContainerName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => new CloudBlobPath(@"container-/blob"), "Invalid container name: container-");
        }

        [Fact]
        public void BackslashInBlobName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => new CloudBlobPath(@"container/my\name"), "The given blob name 'my\\name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [Fact]
        public void OpenSquareBracketInBlobName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => new CloudBlobPath(@"container/my[name"), "The given blob name 'my[name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [Fact]
        public void CloseSquareBracketInBlobName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => new CloudBlobPath(@"container/my]name"), "The given blob name 'my]name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [Fact]
        public void HierarchicalBlobName_IsAllowed()
        {
            new CloudBlobPath(@"container/my/blob");
        }
    }
}
