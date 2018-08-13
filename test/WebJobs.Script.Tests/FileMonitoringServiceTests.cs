// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FileMonitoringServiceTests
    {
        [Theory]
        [InlineData(@"C:\Functions\Scripts\Shared\Test.csx", "Shared")]
        [InlineData(@"C:\Functions\Scripts\Shared\Sub1\Sub2\Test.csx", "Shared")]
        [InlineData(@"C:\Functions\Scripts\Shared", "Shared")]
        public static void GetRelativeDirectory_ReturnsExpectedDirectoryName(string path, string expected)
        {
            Assert.Equal(expected, FileMonitoringService.GetRelativeDirectory(path, @"C:\Functions\Scripts"));
        }
    }
}
