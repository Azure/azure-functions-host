// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Blobs
{
    public class BlobPathTests 
    {
        [Fact]
        public void ParseAndValidate_IfInvalidContainerName_ThrowsFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => BlobPath.ParseAndValidate(@"container-/blob"), "Invalid container name: container-");
        }

        [Fact]
        public void ParseAndValidate_IfBackslashInBlobName_ThrowsFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => BlobPath.ParseAndValidate(@"container/my\name"), "The given blob name 'my\\name' contain illegal characters. A blob name cannot the following character(s): '\\'.");
        }

        [Fact]
        public void ParseAndValidate_IfHierarchicalBlobName_ReturnsBlobPath()
        {
            BlobPath blobPath = BlobPath.ParseAndValidate(@"container/my/blob");

            Assert.NotNull(blobPath);
        }

        [Fact]
        public void ToString_BlobPath_ReturnsExpectedResult()
        {
            BlobPath path = BlobPath.Parse("container/blob", false);
            Assert.Equal("container/blob", path.ToString());

            // '[' ad ']' are valid in blob names
            path = BlobPath.Parse("container/blob[0]", false);
            Assert.Equal("container/blob[0]", path.ToString());

            path = BlobPath.Parse("container", true);
            Assert.Equal("container", path.ToString());

            path = new BlobPath("container", null);
            Assert.Equal("container", path.ToString());
        }
    }
}
