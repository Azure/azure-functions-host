// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebApiApplicationTests
    {
        [Theory]
        [InlineData(@"c:\somedirectory", @"c:\testpath", @"c:\somedirectory;c:\testpath")]
        [InlineData(@"c:\somedirectory", "", @"c:\somedirectory")]
        [InlineData(@"c:\somedirectory", null, @"c:\somedirectory")]
        [InlineData("", @"c:\testpath", @"c:\testpath")]
        [InlineData(null, @"c:\testpath", @"c:\testpath")]
        public void GetShadowCopyPath_ReturnsExpectedPath(string currentShadowCopy, string scriptPath, string expected)
        {
            string updatedPath = WebApiApplication.GetShadowCopyPath(currentShadowCopy, scriptPath);
            string expectedPath = string.Join(";", new[] { currentShadowCopy, scriptPath }.Where(s => !string.IsNullOrEmpty(s)));

            Assert.Equal(expectedPath, updatedPath);
        }
    }
}
