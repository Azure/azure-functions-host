// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class FileLoggerProviderTests
    {
        [Theory]
        [InlineData("Worker.Java.12345", @"Worker\Java")]
        [InlineData("Function.HttpTrigger", @"Function\HttpTrigger")]
        [InlineData("Function.HttpTrigger.User", @"Function\HttpTrigger")]
        [InlineData("Host.General", null)]
        public void CreateLogger_GetsCorrectPath(string category, string expectedPath)
        {
            Assert.Equal(expectedPath, FunctionFileLoggerProvider.GetFilePath(category));
        }

        [Fact]
        public void CreateLogger_UsesSameFileWriter_ForSameFile()
        {
            var rootPath = Path.GetTempPath();
            using (var provider = new FunctionFileLoggerProvider(rootPath, () => true, () => true))
            {
                provider.CreateLogger(LogCategories.CreateFunctionCategory("Test1"));
                provider.CreateLogger(LogCategories.CreateFunctionUserCategory("Test1"));
                provider.CreateLogger(LogCategories.CreateFunctionCategory("Test1"));

                Assert.Single(provider.FileWriterCache);

                // This creates a new entry.
                provider.CreateLogger(LogCategories.CreateFunctionCategory("Test2"));

                Assert.Equal(2, provider.FileWriterCache.Count);
                Assert.NotSame(
                    provider.FileWriterCache[Path.Combine("Function", "Test1")],
                    provider.FileWriterCache[Path.Combine("Function", "Test2")]);
            }
        }
    }
}
