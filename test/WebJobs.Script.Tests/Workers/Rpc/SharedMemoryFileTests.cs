// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class SharedMemoryFileTests
    {
        [Fact(Skip = "NotImplemented")]
        public void Create_VerifyCreated()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void Create_InvalidSize_ThrowsException()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void Open_VerifyOpened()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void Open_NotExists_ThrowsException()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void CreateOrOpen_NotExists_VerifyCreated()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void CreateOrOpen_Exists_VerifyOpened()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void CreateWithContent_VerifyCreatedWithContent()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void GetContentLength_VerifyContentLengthMatches()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void ReadContentAsByteArray_VerifyContentMatches()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void GetContentStream_VerifyStreamContentMatches()
        {
        }

        [Fact(Skip = "NotImplemented")]
        public void DropAllReferencesToMemoryMappedFile_VerifyMemoryMappedFileNotExists()
        {
            // TODO gochaudh: Valid only for Windows. On Linux, the file must also be deleted.
            // TODO gochaudh: Should we skip this test on Linux or also delete the file?
        }

        [Fact(Skip = "NotImplemented")]
        public void Dispose_VerifyDisposedAndMemoryMappedFileNotExists()
        {
        }
    }
}
