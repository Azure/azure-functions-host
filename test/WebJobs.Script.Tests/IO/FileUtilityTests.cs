// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.IO
{
    public class FileUtilityTests
    {
        [Theory]
        [InlineData(@"c:\test\path\", @"c:\test\path\to\file.txt", @"to\file.txt")]
        [InlineData(@"c:\test\path", @"c:\test\path\to\file.txt", @"to\file.txt")]
        [InlineData(@"c:\test\path\", @"c:\test\path\to\some\directory\", @"to\some\directory\")]
        [InlineData(@"c:\test\path\", @"c:\test\path\to\some\directory", @"to\some\directory\")]
        public void GetRelativePath_ReturnsExpectedPath(string path1, string path2, string expectedPath)
        {
            string result = FileUtility.GetRelativePath(path1, path2);

            Assert.Equal(expectedPath, result);
        }

        [Theory]
        [InlineData("file/name", false)]
        [InlineData("file\0name", false)]
        [InlineData("filename", true)]
        public void IsValidFileName_ReturnsBool(string input, bool expectedOutput)
        {
            var result = FileUtility.IsValidFileName(input);
            Assert.Equal(expectedOutput, result);
        }
    }
}
