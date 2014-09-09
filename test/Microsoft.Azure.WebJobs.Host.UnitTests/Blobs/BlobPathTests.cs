// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
            ExceptionAssert.ThrowsFormat(() => BlobPath.ParseAndValidate(@"container/my\name"), "The given blob name 'my\\name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [Fact]
        public void ParseAndValidate_IfOpenSquareBracketInBlobName_ThrowsFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => BlobPath.ParseAndValidate(@"container/my[name"), "The given blob name 'my[name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [Fact]
        public void ParseAndValidate_IfCloseSquareBracketInBlobName_ThrowsFormatException()
        {
            ExceptionAssert.ThrowsFormat(() => BlobPath.ParseAndValidate(@"container/my]name"), "The given blob name 'my]name' contain illegal characters. A blob name cannot the following characters: '\\', '[' and ']'.");
        }

        [Fact]
        public void ParseAndValidate_IfHierarchicalBlobName_ReturnsBlobPath()
        {
            BlobPath blobPath = BlobPath.ParseAndValidate(@"container/my/blob");

            Assert.NotNull(blobPath);
        }
    }
}
