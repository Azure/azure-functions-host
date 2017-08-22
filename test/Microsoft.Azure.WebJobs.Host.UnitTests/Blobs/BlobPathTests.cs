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
        public void CreateBlobWithValidPattern()
        {
            // one parameter
            Assert.Equal("{url}", BindableBlobPath.Create("{url}").ToString());
            // one parameter with dot operator
            Assert.Equal("{a.b.c}", BindableBlobPath.Create("{a.b.c}").ToString());
            // full blob url
            Assert.Equal("archivecontainershun/canaryeh/test/0/2017/08/01/00/09/27.avro",
                BindableBlobPath.Create("https://shunsouthcentralus.blob.core.windows.net/archivecontainershun/canaryeh/test/0/2017/08/01/00/09/27.avro").ToString());
            // invalid partial blob url string
            ExceptionAssert.ThrowsFormat(() => BindableBlobPath.Create("shunsouthcentralus.blob.core.windows.net/container/blob"), "Invalid container name: shunsouthcentralus.blob.core.windows.net");
            // more than one parameter
            ExceptionAssert.ThrowsFormat(() => BindableBlobPath.Create("{hostName}+{Container}+{Blob}"), "Invalid blob path '{hostName}+{Container}+{Blob}'. Paths must be in the format 'container/blob' or 'blob Url'.");
        }
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
            BlobPath path = null;
            BlobPath.TryParse("container/blob", false, out path);
            Assert.Equal("container/blob", path.ToString());

            // '[' ad ']' are valid in blob names
            BlobPath.TryParse("container/blob[0]", false, out path);
            Assert.Equal("container/blob[0]", path.ToString());

            BlobPath.TryParse("container", true, out path);
            Assert.Equal("container", path.ToString());

            path = new BlobPath("container", null);
            Assert.Equal("container", path.ToString());
        }
    }
}
