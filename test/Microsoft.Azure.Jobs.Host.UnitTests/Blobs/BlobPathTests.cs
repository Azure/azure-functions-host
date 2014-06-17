using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.Azure.Jobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Blobs
{
    public class BlobPathTests 
    {
        [Fact]
        public void InvalidContainerName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => BlobPath.ParseAndValidate(@"container-/blob"), "Invalid container name: container-");
        }

        [Fact]
        public void BackslashInBlobName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => BlobPath.ParseAndValidate(@"container/my\name"), "The given blob name 'my\\name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [Fact]
        public void OpenSquareBracketInBlobName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => BlobPath.ParseAndValidate(@"container/my[name"), "The given blob name 'my[name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [Fact]
        public void CloseSquareBracketInBlobName_ShouldThrowFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => BlobPath.ParseAndValidate(@"container/my]name"), "The given blob name 'my]name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [Fact]
        public void HierarchicalBlobName_IsAllowed()
        {
            BlobPath.ParseAndValidate(@"container/my/blob");
        }
    }
}
