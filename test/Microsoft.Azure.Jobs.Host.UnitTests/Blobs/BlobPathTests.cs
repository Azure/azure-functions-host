// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Blobs
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
